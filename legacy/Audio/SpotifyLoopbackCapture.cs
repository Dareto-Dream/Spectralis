using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;
using NAudio.Wasapi.CoreAudioApi.Interfaces;
using NAudio.Wave;
using System.Runtime.InteropServices;

namespace Spectralis;

internal sealed class SpotifyLoopbackCapture : IDisposable
{
    private WasapiLoopbackCapture? systemCapture;
    private ProcessLoopbackCapture? processCapture;
    private VisualizerSampleProvider? target;
    private bool disposed;

    public string LastStartMode { get; private set; } = "not-started";

    public bool Start(
        VisualizerSampleProvider visualizer,
        bool preferProcessTree = true,
        int? processId = null,
        bool allowSystemFallback = true)
    {
        Stop();
        target = visualizer;
        LastStartMode = "starting";

        if (preferProcessTree && ProcessLoopbackCapture.IsSupported)
        {
            try
            {
                var captureProcessId = processId ?? Environment.ProcessId;
                processCapture = new ProcessLoopbackCapture(captureProcessId);
                processCapture.Start(visualizer);
                LastStartMode = $"process-loopback:{captureProcessId}";
                return true;
            }
            catch (Exception ex)
            {
                processCapture?.Dispose();
                processCapture = null;
                LastStartMode = $"process-loopback-failed:{ex.GetType().Name}:{ex.Message}";

                if (!allowSystemFallback)
                    return false;
            }
        }
        else if (preferProcessTree)
        {
            LastStartMode = "process-loopback-unsupported";
        }

        try
        {
            systemCapture = new WasapiLoopbackCapture();
            systemCapture.DataAvailable += OnData;
            systemCapture.StartRecording();
            LastStartMode = LastStartMode == "process-loopback-unsupported"
                ? "system-loopback:process-loopback-unsupported"
                : "system-loopback";
            return true;
        }
        catch (Exception ex)
        {
            systemCapture?.Dispose();
            systemCapture = null;
            LastStartMode = $"failed:{ex.GetType().Name}:{ex.Message}";
            return false;
        }
    }

    public void Stop()
    {
        if (processCapture is not null)
        {
            processCapture.Dispose();
            processCapture = null;
        }

        if (systemCapture is null) return;
        systemCapture.DataAvailable -= OnData;
        try { systemCapture.StopRecording(); } catch { }
        systemCapture.Dispose();
        systemCapture = null;
    }

    private void OnData(object? sender, WaveInEventArgs e)
    {
        if (target is null || systemCapture is null || e.BytesRecorded == 0) return;

        var fmt = systemCapture.WaveFormat;
        var ch = Math.Max(1, fmt.Channels);

        if (fmt.Encoding == WaveFormatEncoding.IeeeFloat && fmt.BitsPerSample == 32)
        {
            var count = e.BytesRecorded / 4;
            var floats = new float[count];
            Buffer.BlockCopy(e.Buffer, 0, floats, 0, e.BytesRecorded);
            target.FeedExternalSamples(floats, 0, count, ch);
        }
        else if (fmt.BitsPerSample == 16)
        {
            var count = e.BytesRecorded / 2;
            var floats = new float[count];
            for (var i = 0; i < count; i++)
                floats[i] = BitConverter.ToInt16(e.Buffer, i * 2) / 32768f;
            target.FeedExternalSamples(floats, 0, count, ch);
        }
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        Stop();
    }

    private sealed class ProcessLoopbackCapture : IDisposable
    {
        private const string VirtualAudioDeviceProcessLoopback = "VIRTUAL_AUDIO_DEVICE_PROCESS_LOOPBACK";
        private const ushort VariantTypeBlob = 65;
        private static readonly Guid AudioClientGuid = typeof(IAudioClient).GUID;

        private readonly int processId;
        private AudioClient? audioClient;
        private EventWaitHandle? captureEvent;
        private Thread? captureThread;
        private VisualizerSampleProvider? target;
        private WaveFormat waveFormat = new(44100, 16, 2);
        private byte[] recordBuffer = [];
        private float[] sampleBuffer = [];
        private int bytesPerFrame;
        private volatile bool stopping;
        private bool disposed;

        public ProcessLoopbackCapture(int processId)
        {
            this.processId = processId;
        }

        public static bool IsSupported => OperatingSystem.IsWindowsVersionAtLeast(10, 0, 20348);

        public void Start(VisualizerSampleProvider visualizer)
        {
            target = visualizer;
            stopping = false;

            audioClient = ActivateProcessLoopbackAudioClientAsync(processId).GetAwaiter().GetResult();
            waveFormat = new WaveFormat(44100, 16, 2);
            bytesPerFrame = waveFormat.BlockAlign;

            var streamFlags =
                AudioClientStreamFlags.Loopback |
                AudioClientStreamFlags.EventCallback |
                AudioClientStreamFlags.AutoConvertPcm |
                AudioClientStreamFlags.SrcDefaultQuality;

            audioClient.Initialize(
                AudioClientShareMode.Shared,
                streamFlags,
                0,
                0,
                waveFormat,
                Guid.Empty);

            captureEvent = new EventWaitHandle(false, EventResetMode.AutoReset);
            audioClient.SetEventHandle(captureEvent.SafeWaitHandle.DangerousGetHandle());

            recordBuffer = new byte[Math.Max(bytesPerFrame, audioClient.BufferSize * bytesPerFrame)];
            captureThread = new Thread(CaptureThread)
            {
                IsBackground = true,
                Name = "Spectralis process audio capture"
            };
            captureThread.Start();
        }

        private void CaptureThread()
        {
            try
            {
                if (audioClient is null || captureEvent is null)
                    return;

                using var captureClient = audioClient.AudioCaptureClient;
                audioClient.Start();

                while (!stopping)
                {
                    captureEvent.WaitOne(250);
                    if (stopping)
                        break;

                    ReadAvailablePackets(captureClient);
                }
            }
            catch
            {
                // Capture is best-effort; playback should continue even if the OS loopback stream fails.
            }
            finally
            {
                try { audioClient?.Stop(); } catch { }
            }
        }

        private void ReadAvailablePackets(AudioCaptureClient captureClient)
        {
            while (!stopping && captureClient.GetNextPacketSize() > 0)
            {
                var buffer = captureClient.GetBuffer(
                    out var framesAvailable,
                    out var flags);

                try
                {
                    if (framesAvailable <= 0)
                        continue;

                    var bytesAvailable = framesAvailable * bytesPerFrame;
                    if (recordBuffer.Length < bytesAvailable)
                        Array.Resize(ref recordBuffer, bytesAvailable);

                    if ((flags & AudioClientBufferFlags.Silent) == AudioClientBufferFlags.Silent)
                        Array.Clear(recordBuffer, 0, bytesAvailable);
                    else
                        Marshal.Copy(buffer, recordBuffer, 0, bytesAvailable);

                    FeedPcm16(recordBuffer, bytesAvailable);
                }
                finally
                {
                    captureClient.ReleaseBuffer(framesAvailable);
                }
            }
        }

        private void FeedPcm16(byte[] buffer, int bytesAvailable)
        {
            if (target is null || bytesAvailable <= 0)
                return;

            var sampleCount = bytesAvailable / 2;
            if (sampleBuffer.Length < sampleCount)
                Array.Resize(ref sampleBuffer, sampleCount);

            for (var i = 0; i < sampleCount; i++)
                sampleBuffer[i] = BitConverter.ToInt16(buffer, i * 2) / 32768f;

            target.FeedExternalSamples(sampleBuffer, 0, sampleCount, Math.Max(1, waveFormat.Channels));
        }

        private static async Task<AudioClient> ActivateProcessLoopbackAudioClientAsync(int processId)
        {
            var activationParams = new AudioClientActivationParams
            {
                ActivationType = AudioClientActivationTypeValue.ProcessLoopback,
                TargetProcessId = (uint)Math.Max(0, processId),
                ProcessLoopbackMode = ProcessLoopbackModeValue.IncludeTargetProcessTree
            };

            var activationParamsSize = Marshal.SizeOf<AudioClientActivationParams>();
            var activationParamsPtr = Marshal.AllocHGlobal(activationParamsSize);
            var propVariantPtr = Marshal.AllocHGlobal(Marshal.SizeOf<BlobPropVariant>());
            IActivateAudioInterfaceAsyncOperation? operation = null;

            try
            {
                Marshal.StructureToPtr(activationParams, activationParamsPtr, false);
                Marshal.StructureToPtr(
                    new BlobPropVariant
                    {
                        VariantType = VariantTypeBlob,
                        BlobSize = activationParamsSize,
                        BlobData = activationParamsPtr
                    },
                    propVariantPtr,
                    false);

                var completionHandler = new ActivateCompletionHandler();
                var result = ActivateAudioInterfaceAsync(
                    VirtualAudioDeviceProcessLoopback,
                    AudioClientGuid,
                    propVariantPtr,
                    completionHandler,
                    out operation);

                if (result < 0)
                    Marshal.ThrowExceptionForHR(result);

                return new AudioClient(await completionHandler.Task.WaitAsync(TimeSpan.FromSeconds(5)));
            }
            finally
            {
                if (operation is not null && Marshal.IsComObject(operation))
                    Marshal.ReleaseComObject(operation);

                Marshal.FreeHGlobal(propVariantPtr);
                Marshal.FreeHGlobal(activationParamsPtr);
            }
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            stopping = true;

            try { captureEvent?.Set(); } catch { }

            if (captureThread is not null && captureThread.IsAlive && captureThread != Thread.CurrentThread)
            {
                try { captureThread.Join(750); } catch { }
            }

            try { audioClient?.Stop(); } catch { }
            audioClient?.Dispose();
            captureEvent?.Dispose();
            audioClient = null;
            captureEvent = null;
            captureThread = null;
            target = null;
        }

        [DllImport("Mmdevapi.dll", ExactSpelling = true, PreserveSig = true)]
        private static extern int ActivateAudioInterfaceAsync(
            [MarshalAs(UnmanagedType.LPWStr)] string deviceInterfacePath,
            [MarshalAs(UnmanagedType.LPStruct)] Guid riid,
            IntPtr activationParams,
            IActivateAudioInterfaceCompletionHandler completionHandler,
            out IActivateAudioInterfaceAsyncOperation activationOperation);

        [StructLayout(LayoutKind.Sequential)]
        private struct BlobPropVariant
        {
            public ushort VariantType;
            public ushort Reserved1;
            public ushort Reserved2;
            public ushort Reserved3;
            public int BlobSize;
            public IntPtr BlobData;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct AudioClientActivationParams
        {
            public AudioClientActivationTypeValue ActivationType;
            public uint TargetProcessId;
            public ProcessLoopbackModeValue ProcessLoopbackMode;
        }

        private enum AudioClientActivationTypeValue
        {
            ProcessLoopback = 1
        }

        private enum ProcessLoopbackModeValue
        {
            IncludeTargetProcessTree = 0
        }

        [Guid("94EA2B94-E9CC-49E0-C0FF-EE64CA8F5B90")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IAgileObject
        {
        }

        private sealed class ActivateCompletionHandler :
            IActivateAudioInterfaceCompletionHandler,
            IAgileObject
        {
            private readonly TaskCompletionSource<IAudioClient> completion = new(
                TaskCreationOptions.RunContinuationsAsynchronously);

            public Task<IAudioClient> Task => completion.Task;

            public void ActivateCompleted(IActivateAudioInterfaceAsyncOperation activateOperation)
            {
                try
                {
                    activateOperation.GetActivateResult(out var result, out var audioInterface);
                    if (result < 0)
                    {
                        completion.TrySetException(Marshal.GetExceptionForHR(result) ?? new COMException("Process loopback activation failed.", result));
                        return;
                    }

                    if (audioInterface is IAudioClient audioClient)
                    {
                        completion.TrySetResult(audioClient);
                        return;
                    }

                    completion.TrySetException(new InvalidCastException("Process loopback activation did not return an audio client."));
                }
                catch (Exception ex)
                {
                    completion.TrySetException(ex);
                }
            }
        }
    }
}
