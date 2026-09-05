using NAudio.CoreAudioApi;
using NAudio.Wave;
using Spectralis.Core.Platform;
using Spectralis.Core.Visualizers;

namespace Spectralis.Core.Audio.Loopback;

/// <summary>
/// Windows backend: WASAPI process-tree loopback (Win10 20348+) with
/// system-loopback fallback — the unchanged legacy SpotifyLoopbackCapture path
/// behind the ILoopbackCaptureSource seam.
/// </summary>
[System.Runtime.Versioning.SupportedOSPlatform("windows")]
public sealed class WindowsLoopbackCaptureSource : ILoopbackCaptureSource
{
    private WasapiLoopbackCapture? systemCapture;
    private ProcessLoopbackCapture? processCapture;
    private VisualizerSampleProvider? target;
    private bool disposed;

    public bool IsSupported => OperatingSystem.IsWindows();

    public string StatusDetail => LastStartMode;

    public string LastStartMode { get; private set; } = "not-started";

    public bool Start(VisualizerSampleProvider visualizer, int? targetProcessId = null) =>
        Start(visualizer, preferProcessTree: true, targetProcessId, allowSystemFallback: true);

    public bool Start(
        VisualizerSampleProvider visualizer,
        bool preferProcessTree,
        int? processId,
        bool allowSystemFallback)
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
                processCapture.Start((buf, off, cnt, ch) => visualizer.FeedExternalSamples(buf, off, cnt, ch));
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
}
