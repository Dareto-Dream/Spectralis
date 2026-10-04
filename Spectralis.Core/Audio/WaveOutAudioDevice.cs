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

/// <summary>
/// Stable device ids for WaveOut. winmm only gives a device <em>number</em>, and numbers shift when
/// hardware is plugged or unplugged — so a saved choice is the device <em>name</em> (duplicates get
/// a "#2", "#3" suffix) and the number is looked up again every time a device is created.
/// </summary>
public static class WaveOutDeviceNaming
{
    public const string DefaultId = "-1";

    public static IReadOnlyList<AudioDeviceInfo> Build(IReadOnlyList<string> productNames)
    {
        var devices = new List<AudioDeviceInfo> { new(DefaultId, "System default", IsDefault: true) };
        var seen = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in productNames)
        {
            var clean = string.IsNullOrWhiteSpace(name) ? "Audio device" : name.Trim();
            seen[clean] = seen.GetValueOrDefault(clean) + 1;
            var id = seen[clean] == 1 ? clean : $"{clean} #{seen[clean]}";
            devices.Add(new AudioDeviceInfo(id, id, IsDefault: false));
        }
        return devices;
    }

    /// <summary>Maps a saved id to the current winmm device number; unknown or default ids give -1.</summary>
    public static int ResolveNumber(string? id, IReadOnlyList<string> productNames)
    {
        if (string.IsNullOrWhiteSpace(id) || id == DefaultId)
            return -1;

        var devices = Build(productNames);
        for (var i = 1; i < devices.Count; i++)
        {
            if (string.Equals(devices[i].Id, id, StringComparison.OrdinalIgnoreCase))
                return i - 1;
        }
        return -1;
    }
}

public sealed class WaveOutDeviceEnumerator : IAudioDeviceEnumerator
{
    public IReadOnlyList<AudioDeviceInfo> GetOutputDevices() =>
        WaveOutDeviceNaming.Build(ReadProductNames());

    public IAudioDevice CreateDevice(string? deviceId, int latencyMs)
    {
        // A device that's gone since it was chosen quietly plays through the default instead.
        var number = WaveOutDeviceNaming.ResolveNumber(deviceId, ReadProductNames());
        return new WaveOutAudioDevice(number.ToString(System.Globalization.CultureInfo.InvariantCulture), latencyMs);
    }

    private static IReadOnlyList<string> ReadProductNames()
    {
        var names = new List<string>();
        if (!OperatingSystem.IsWindows())
            return names;

        try
        {
            var count = WaveInterop.waveOutGetNumDevs();
            for (var i = 0; i < count; i++)
            {
                var result = WaveInterop.waveOutGetDevCaps((IntPtr)i, out var caps,
                    System.Runtime.InteropServices.Marshal.SizeOf<WaveOutCapabilities>());
                names.Add((int)result == 0 ? caps.ProductName : string.Empty);
            }
        }
        catch
        {
            // winmm unavailable: behave as "default only".
        }
        return names;
    }
}
