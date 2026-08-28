using System.Diagnostics;
using System.Runtime.InteropServices;
using Spectralis.Core.Integrations;
using Spectralis.Core.Platform;

namespace Spectralis.Core.Audio;

/// <summary>
/// macOS implementation of <see cref="IAudioDevice"/>: streams raw float32 PCM to
/// the bundled ffmpeg over stdin, which renders it through its AudioToolbox output
/// device. Deliberately the same subprocess shape as
/// <see cref="PulseAudioAudioDevice"/> on Linux — every platform leg drives a
/// bundled helper binary rather than talking to an OS audio API directly, so there
/// is one thing to reason about instead of three.
/// </summary>
public sealed class AudioToolboxAudioDevice : IAudioDevice
{
    private readonly int _latencyMs;
    private IAudioSampleSource? _source;
    private Process? _process;
    private Thread? _writeThread;
    private volatile bool _playing;
    private volatile bool _stopping;
    private float _volume = 0.85f;
    private bool _disposed;

    public AudioToolboxAudioDevice(string? deviceId = null, int latencyMs = 70)
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

        // Resume rather than spawning a second ffmpeg: Play() after Pause() must not
        // leave an orphaned process holding the output device.
        if (_process is { HasExited: false })
        {
            _playing = true;
            return;
        }

        var ffmpeg = FfmpegLocator.FindExecutable();
        if (ffmpeg is null)
        {
            PlaybackStopped?.Invoke(this, new AudioDeviceStoppedEventArgs(
                new FileNotFoundException(
                    "FFmpeg not found. Place 'ffmpeg' in the Spectralis application folder, " +
                    "or install FFmpeg and add it to your system PATH.")));
            return;
        }

        var psi = new ProcessStartInfo
        {
            FileName = ffmpeg,
            RedirectStandardInput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        psi.ArgumentList.Add("-hide_banner");
        psi.ArgumentList.Add("-loglevel");
        psi.ArgumentList.Add("error");
        // Raw PCM needs no probing; skipping it keeps startup latency down.
        psi.ArgumentList.Add("-fflags");
        psi.ArgumentList.Add("nobuffer");
        psi.ArgumentList.Add("-f");
        psi.ArgumentList.Add("f32le");
        psi.ArgumentList.Add("-ar");
        psi.ArgumentList.Add(_source.SampleRate.ToString());
        psi.ArgumentList.Add("-ac");
        psi.ArgumentList.Add(Math.Max(_source.Channels, 1).ToString());
        psi.ArgumentList.Add("-i");
        psi.ArgumentList.Add("-");
        psi.ArgumentList.Add("-f");
        psi.ArgumentList.Add("audiotoolbox");
        psi.ArgumentList.Add(DeviceId ?? "default");

        try
        {
            _process = Process.Start(psi) ?? throw new InvalidOperationException("ffmpeg did not start.");
        }
        catch (Exception ex)
        {
            _process = null;
            PlaybackStopped?.Invoke(this, new AudioDeviceStoppedEventArgs(
                new InvalidOperationException(
                    "Couldn't start macOS audio output (bundled 'ffmpeg' with the audiotoolbox device).", ex)));
            return;
        }

        _stopping = false;
        _playing = true;
        _writeThread = new Thread(WriteLoop)
        {
            IsBackground = true,
            Name = "Spectralis audiotoolbox output",
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

            // Let ffmpeg drain what's already queued instead of cutting the tail off.
            if (!_stopping)
            {
                try { stream.Flush(); stream.Close(); } catch { }
                try { process.WaitForExit(2000); } catch { }
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

/// <summary>Per-device selection isn't wired up on macOS yet — same current
/// limitation as <see cref="PulseAudioDeviceEnumerator"/> on Linux.</summary>
public sealed class AudioToolboxDeviceEnumerator : IAudioDeviceEnumerator
{
    public IReadOnlyList<AudioDeviceInfo> GetOutputDevices() =>
        new[] { new AudioDeviceInfo("default", "System default", IsDefault: true) };

    public IAudioDevice CreateDevice(string? deviceId, int latencyMs) =>
        new AudioToolboxAudioDevice(deviceId == "default" ? null : deviceId, latencyMs);
}
