using System.Diagnostics;
using System.Runtime.InteropServices;
using Spectralis.Core.Platform;

namespace Spectralis.Core.Audio;

/// <summary>
/// Linux implementation of <see cref="IAudioDevice"/>: streams raw float32
/// PCM to PulseAudio's `paplay` over stdin (PipeWire's pipewire-pulse ships
/// a compatible binary, so both stacks work with no native P/Invoke). Mirrors
/// the subprocess approach <see cref="Loopback.PulseAudioLoopbackCaptureSource"/>
/// already uses for capture — NAudio's <see cref="WaveOutAudioDevice"/> only
/// works on Windows (it P/Invokes winmm.dll), so this is the seam's Linux leg.
/// </summary>
public sealed class PulseAudioAudioDevice : IAudioDevice
{
    private readonly int _latencyMs;
    private IAudioSampleSource? _source;
    private Process? _process;
    private Thread? _writeThread;
    private volatile bool _playing;
    private volatile bool _stopping;
    private float _volume = 0.85f;
    private bool _disposed;

    public PulseAudioAudioDevice(string? deviceId = null, int latencyMs = 70)
    {
        DeviceId = deviceId;
        _latencyMs = latencyMs;
    }

    public string? DeviceId { get; }

    public int SampleRate => _source?.SampleRate ?? 0;
    public int Channels => _source?.Channels ?? 0;

    public float Volume
    {
        get => _volume;
        set => _volume = Math.Clamp(value, 0f, 1f);
    }

    public bool IsPlaying => _playing;

    public event EventHandler<AudioDeviceStoppedEventArgs>? PlaybackStopped;

    public void Init(IAudioSampleSource source)
    {
        Stop();
        _source = source;
    }

    public void Play()
    {
        if (_playing || _source is null)
        {
            return;
        }

        var psi = new ProcessStartInfo
        {
            FileName = "paplay",
            RedirectStandardInput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        psi.ArgumentList.Add("--raw");
        psi.ArgumentList.Add("--format=float32le");
        psi.ArgumentList.Add($"--rate={_source.SampleRate}");
        psi.ArgumentList.Add($"--channels={_source.Channels}");
        psi.ArgumentList.Add($"--latency-msec={_latencyMs}");
        psi.ArgumentList.Add("--client-name=Spectralis");
        if (DeviceId is not null)
        {
            psi.ArgumentList.Add($"--device={DeviceId}");
        }

        try
        {
            _process = Process.Start(psi) ?? throw new InvalidOperationException("paplay did not start.");
        }
        catch (Exception ex)
        {
            _process = null;
            PlaybackStopped?.Invoke(this, new AudioDeviceStoppedEventArgs(
                new InvalidOperationException(
                    "Couldn't start PulseAudio output ('paplay'). Install the 'pulseaudio-utils' " +
                    "(or 'pipewire-pulse') package to play audio on Linux.", ex)));
            return;
        }

        _stopping = false;
        _playing = true;
        _writeThread = new Thread(WriteLoop)
        {
            IsBackground = true,
            Name = "Spectralis pulse output",
        };
        _writeThread.Start();
    }

    public void Pause() => _playing = false;

    public void Stop()
    {
        _stopping = true;
        _playing = false;

        var process = _process;
        _process = null;
        if (process is not null)
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
            }
            catch
            {
            }

            process.Dispose();
        }

        if (_writeThread is { IsAlive: true } thread && thread != Thread.CurrentThread)
        {
            try { thread.Join(750); } catch { }
        }

        _writeThread = null;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Stop();
    }

    private void WriteLoop()
    {
        var process = _process;
        var source = _source;
        if (process is null || source is null)
        {
            return;
        }

        var floatBuffer = new float[2048 * Math.Max(source.Channels, 1)];
        var stream = process.StandardInput.BaseStream;

        try
        {
            while (!_stopping)
            {
                if (!_playing)
                {
                    Thread.Sleep(10);
                    continue;
                }

                var read = source.Read(floatBuffer, 0, floatBuffer.Length);
                if (read <= 0)
                {
                    break;
                }

                var volume = _volume;
                if (volume != 1f)
                {
                    for (var i = 0; i < read; i++)
                    {
                        floatBuffer[i] *= volume;
                    }
                }

                stream.Write(MemoryMarshal.AsBytes(floatBuffer.AsSpan(0, read)));
            }
        }
        catch (Exception ex)
        {
            if (!_stopping)
            {
                _playing = false;
                PlaybackStopped?.Invoke(this, new AudioDeviceStoppedEventArgs(ex));
            }

            return;
        }

        if (!_stopping)
        {
            _playing = false;
            PlaybackStopped?.Invoke(this, new AudioDeviceStoppedEventArgs(null));
        }
    }
}

/// <summary>Per-device selection isn't wired up on Linux yet — same current
/// limitation as <see cref="WaveOutDeviceEnumerator"/> on Windows.</summary>
public sealed class PulseAudioDeviceEnumerator : IAudioDeviceEnumerator
{
    public IReadOnlyList<AudioDeviceInfo> GetOutputDevices() =>
        new[] { new AudioDeviceInfo("default", "System default", IsDefault: true) };

    public IAudioDevice CreateDevice(string? deviceId, int latencyMs) =>
        new PulseAudioAudioDevice(deviceId == "default" ? null : deviceId, latencyMs);
}
