using NAudio.Wave;
using Spectralis.Core.Platform;

namespace Spectralis.Core.Audio;

/// <summary>
/// Windows implementation of <see cref="IAudioDevice"/> over NAudio's WaveOutEvent —
/// the same output path the WinForms app used (70 ms latency, 3 buffers).
/// </summary>
public sealed class WaveOutAudioDevice : IAudioDevice
{
    private readonly int _latencyMs;
    private WaveOutEvent? _output;
    private SampleSourceProvider? _provider;
    private float _volume = 0.85f;

    public WaveOutAudioDevice(string? deviceId = null, int latencyMs = 70)
    {
        DeviceId = deviceId;
        _latencyMs = latencyMs;
    }

    public string? DeviceId { get; }

    public int SampleRate => _provider?.WaveFormat.SampleRate ?? 0;
    public int Channels => _provider?.WaveFormat.Channels ?? 0;

    public float Volume
    {
        get => _volume;
        set
        {
            _volume = Math.Clamp(value, 0f, 1f);
            if (_output is not null)
            {
                _output.Volume = _volume;
            }
        }
    }

    public bool IsPlaying => _output?.PlaybackState == NAudio.Wave.PlaybackState.Playing;

    public event EventHandler<AudioDeviceStoppedEventArgs>? PlaybackStopped;

    public void Init(IAudioSampleSource source)
    {
        DisposeOutput();

        _provider = new SampleSourceProvider(source);
        _output = new WaveOutEvent
        {
            DesiredLatency = _latencyMs,
            NumberOfBuffers = 3,
            Volume = _volume,
        };
        if (DeviceId is not null && int.TryParse(DeviceId, out var deviceNumber))
        {
            _output.DeviceNumber = deviceNumber;
        }

        _output.PlaybackStopped += (_, e) =>
            PlaybackStopped?.Invoke(this, new AudioDeviceStoppedEventArgs(e.Exception));
        _output.Init(_provider);
    }

    public void Play() => _output?.Play();
    public void Pause() => _output?.Pause();
    public void Stop() => _output?.Stop();

    public void Dispose() => DisposeOutput();

    private void DisposeOutput()
    {
        _output?.Dispose();
        _output = null;
        _provider = null;
    }

    /// <summary>Adapts the engine-facing sample source to NAudio's ISampleProvider.</summary>
    private sealed class SampleSourceProvider : ISampleProvider
    {
        private readonly IAudioSampleSource _source;

        public SampleSourceProvider(IAudioSampleSource source)
        {
            _source = source;
            WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(source.SampleRate, source.Channels);
        }

        public WaveFormat WaveFormat { get; }

        public int Read(float[] buffer, int offset, int count) => _source.Read(buffer, offset, count);
    }
}

public sealed class WaveOutDeviceEnumerator : IAudioDeviceEnumerator
{
    public IReadOnlyList<AudioDeviceInfo> GetOutputDevices() =>
        // The winmm enumeration API isn't exposed on the netstandard NAudio surface;
        // the legacy app always played to the default device. Per-device selection
        // arrives with the WASAPI backend in the Settings audio page.
        new[] { new AudioDeviceInfo("-1", "System default", IsDefault: true) };

    public IAudioDevice CreateDevice(string? deviceId, int latencyMs) =>
        new WaveOutAudioDevice(deviceId, latencyMs);
}
