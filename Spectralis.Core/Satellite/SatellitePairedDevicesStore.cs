using System.Text.Json;
using System.Text.Json.Serialization;

namespace Spectralis.Core.Satellite;

public sealed record SatellitePairedDevice(string DeviceId, string DisplayName, DateTimeOffset PairedAtUtc);

/// <summary>
/// Persists which receiver devices have completed the PIN pairing handshake, so returning
/// devices skip straight to reconnecting instead of asking for the PIN again every time —
/// mirrors <c>CreatorTrustStore</c>'s shape (file-backed, in-memory cache, explicit test-seam
/// constructor param rather than a static override).
/// </summary>
public sealed class SatellitePairedDevicesStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly string _storePath;
    private readonly object _gate = new();
    private List<SatellitePairedDevice> _devices = [];
    private bool _loaded;

    public SatellitePairedDevicesStore(string? storePath = null)
    {
        _storePath = storePath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Spectralis",
            "satellite-paired-devices.json");
    }

    public bool IsPaired(string deviceId)
    {
        lock (_gate)
        {
            EnsureLoaded();
            return _devices.Any(d => d.DeviceId == deviceId);
        }
    }

    public void MarkPaired(string deviceId, string displayName)
    {
        lock (_gate)
        {
            EnsureLoaded();
            _devices.RemoveAll(d => d.DeviceId == deviceId);
            _devices.Add(new SatellitePairedDevice(deviceId, displayName, DateTimeOffset.UtcNow));
            Save();
        }
    }

    public void Forget(string deviceId)
    {
        lock (_gate)
        {
            EnsureLoaded();
            if (_devices.RemoveAll(d => d.DeviceId == deviceId) > 0)
            {
                Save();
            }
        }
    }

    public IReadOnlyList<SatellitePairedDevice> AllDevices
    {
        get
        {
            lock (_gate)
            {
                EnsureLoaded();
                return [.. _devices];
            }
        }
    }

    private void EnsureLoaded()
    {
        if (_loaded)
        {
            return;
        }

        _loaded = true;
        if (!File.Exists(_storePath))
        {
            return;
        }

        try
        {
            var json = File.ReadAllText(_storePath);
            _devices = JsonSerializer.Deserialize<List<SatellitePairedDevice>>(json, JsonOptions) ?? [];
        }
        catch
        {
            _devices = [];
        }
    }

    private void Save()
    {
        try
        {
            var dir = Path.GetDirectoryName(_storePath);
            if (!string.IsNullOrWhiteSpace(dir))
            {
                Directory.CreateDirectory(dir);
            }

            File.WriteAllText(_storePath, JsonSerializer.Serialize(_devices, JsonOptions));
        }
        catch
        {
            // Persistence failure is non-fatal — the device just re-pairs next connection.
        }
    }
}
