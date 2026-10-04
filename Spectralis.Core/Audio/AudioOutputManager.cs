using Spectralis.Core.Platform;

namespace Spectralis.Core.Audio;

public enum AudioOutputNoticeKind
{
    /// <summary>The chosen device disappeared, so playback moved to the system default.</summary>
    FellBackToDefault,

    /// <summary>The chosen device came back and playback moved onto it again.</summary>
    Restored,
}

public sealed record AudioOutputNotice(AudioOutputNoticeKind Kind, string DeviceName);

/// <summary>
/// Owns which output device the engine plays through. Remembers the user's preferred device even
/// while it's unplugged, falls back to the system default when it vanishes, and moves back when it
/// returns — so a USB DAC or Bluetooth headset coming and going never leaves playback silent or on
/// the wrong device. <see cref="Refresh"/> is polled (the platform backends have no change events).
/// </summary>
public sealed class AudioOutputManager : IDisposable
{
    private readonly AudioEngine _engine;
    private readonly IAudioDeviceEnumerator _enumerator;
    private readonly object _gate = new();
    private Timer? _timer;
    private IReadOnlyList<AudioDeviceInfo> _devices = [];

    public AudioOutputManager(AudioEngine engine, IAudioDeviceEnumerator enumerator, string? preferredDeviceId = null)
    {
        _engine = engine;
        _enumerator = enumerator;
        PreferredDeviceId = Normalize(preferredDeviceId);
        Refresh();
    }

    /// <summary>What the user picked; null means "follow the system default".</summary>
    public string? PreferredDeviceId { get; private set; }

    /// <summary>What the engine is actually playing through right now; null is the system default.</summary>
    public string? ActiveDeviceId { get; private set; }

    /// <summary>True while the user's chosen device is missing and playback is on the default instead.</summary>
    public bool IsFallbackActive => PreferredDeviceId is not null && ActiveDeviceId is null;

    public IReadOnlyList<AudioDeviceInfo> Devices
    {
        get { lock (_gate) return _devices; }
    }

    /// <summary>The device list changed (plugged, unplugged, renamed). May fire on a timer thread.</summary>
    public event Action? DevicesChanged;

    /// <summary>A fallback or restore happened that the user should hear about. May fire on a timer thread.</summary>
    public event Action<AudioOutputNotice>? Notice;

    /// <summary>Starts polling for device changes every <paramref name="interval"/> (default 3s).</summary>
    public void StartWatching(TimeSpan? interval = null)
    {
        var period = interval ?? TimeSpan.FromSeconds(3);
        _timer?.Dispose();
        _timer = new Timer(_ => Refresh(), null, period, period);
    }

    /// <summary>Picks a device (null = system default). Applied now if present, otherwise when it appears.</summary>
    public void SetPreferred(string? deviceId)
    {
        lock (_gate)
            PreferredDeviceId = Normalize(deviceId);
        Refresh();
    }

    public void Refresh()
    {
        IReadOnlyList<AudioDeviceInfo> latest;
        try
        {
            latest = _enumerator.GetOutputDevices();
        }
        catch
        {
            return; // enumeration hiccup: keep what we have rather than flap to default
        }

        AudioOutputNotice? notice = null;
        var changed = false;
        lock (_gate)
        {
            changed = !SameDevices(_devices, latest);
            _devices = latest;

            var preferredPresent = PreferredDeviceId is null || latest.Any(d => IdEquals(d.Id, PreferredDeviceId));
            var desired = preferredPresent ? PreferredDeviceId : null;

            if (!IdEquals(desired, ActiveDeviceId))
            {
                var previous = ActiveDeviceId;
                ActiveDeviceId = desired;
                _engine.SetOutputDevice(desired);

                if (!preferredPresent)
                {
                    notice = new AudioOutputNotice(AudioOutputNoticeKind.FellBackToDefault, PreferredDeviceId!);
                }
                else if (desired is not null && previous is null && changed)
                {
                    notice = new AudioOutputNotice(AudioOutputNoticeKind.Restored, desired);
                }
            }
        }

        if (changed)
            DevicesChanged?.Invoke();
        if (notice is not null)
            Notice?.Invoke(notice);
    }

    public void Dispose()
    {
        _timer?.Dispose();
        _timer = null;
    }

    private static string? Normalize(string? id) =>
        string.IsNullOrWhiteSpace(id) || id == "-1" || id == "default" ? null : id;

    private static bool IdEquals(string? a, string? b) =>
        string.Equals(Normalize(a), Normalize(b), StringComparison.OrdinalIgnoreCase);

    private static bool SameDevices(IReadOnlyList<AudioDeviceInfo> a, IReadOnlyList<AudioDeviceInfo> b) =>
        a.Count == b.Count && a.Zip(b).All(p => p.First == p.Second);
}
