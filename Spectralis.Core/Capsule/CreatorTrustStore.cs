using System.Text.Json;
using System.Text.Json.Serialization;

namespace Spectralis.Core.Capsule;

/// <summary>
/// Persists trusted creator fingerprints plus a local cache of CDN key metadata
/// (the offline revocation cache: revoked entries reject without a network hop).
/// </summary>
public sealed class CreatorTrustStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly string _storePath;
    private readonly object _gate = new();
    private List<CreatorTrustEntry> _entries = [];
    private Dictionary<string, CreatorKeyMetadata> _metadataCache = [];

    public CreatorTrustStore(string? storePath = null)
    {
        _storePath = storePath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Spectralis",
            "trusted-creators.json");
    }

    public IReadOnlyList<CreatorTrustEntry> Entries
    {
        get
        {
            lock (_gate)
            {
                return [.. _entries];
            }
        }
    }

    public void Load()
    {
        lock (_gate)
        {
            try
            {
                if (!File.Exists(_storePath))
                {
                    return;
                }

                var data = JsonSerializer.Deserialize<TrustStoreData>(File.ReadAllText(_storePath), JsonOptions);
                _entries = data?.Entries ?? [];
                _metadataCache = data?.MetadataCache ?? [];
            }
            catch
            {
                _entries = [];
                _metadataCache = [];
            }
        }
    }

    public bool IsTrusted(string fingerprint)
    {
        lock (_gate)
        {
            return _entries.Any(entry =>
                string.Equals(entry.Fingerprint, fingerprint, StringComparison.OrdinalIgnoreCase));
        }
    }

    public void Trust(string fingerprint, string displayName)
    {
        lock (_gate)
        {
            _entries.RemoveAll(entry =>
                string.Equals(entry.Fingerprint, fingerprint, StringComparison.OrdinalIgnoreCase));
            _entries.Add(new CreatorTrustEntry(
                fingerprint, displayName, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, null));
        }

        Save();
    }

    public void Revoke(string fingerprint)
    {
        lock (_gate)
        {
            _entries.RemoveAll(entry =>
                string.Equals(entry.Fingerprint, fingerprint, StringComparison.OrdinalIgnoreCase));
        }

        Save();
    }

    public void CacheMetadata(string fingerprint, CreatorKeyMetadata metadata)
    {
        lock (_gate)
        {
            _metadataCache[fingerprint] = metadata;
            var idx = _entries.FindIndex(entry =>
                string.Equals(entry.Fingerprint, fingerprint, StringComparison.OrdinalIgnoreCase));
            if (idx >= 0)
            {
                _entries[idx] = _entries[idx] with
                {
                    LastValidatedUtc = DateTimeOffset.UtcNow,
                    MetadataCachedAtUtc = DateTimeOffset.UtcNow,
                };
            }
        }

        Save();
    }

    public CreatorKeyMetadata? GetCachedMetadata(string fingerprint)
    {
        lock (_gate)
        {
            return _metadataCache.TryGetValue(fingerprint, out var metadata) ? metadata : null;
        }
    }

    private void Save()
    {
        lock (_gate)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_storePath)!);
                var data = new TrustStoreData
                {
                    Entries = [.. _entries],
                    MetadataCache = new Dictionary<string, CreatorKeyMetadata>(_metadataCache),
                };
                File.WriteAllText(_storePath, JsonSerializer.Serialize(data, JsonOptions));
            }
            catch
            {
                // Persisting trust is best-effort; an unwritable disk must not block playback.
            }
        }
    }

    private sealed class TrustStoreData
    {
        public List<CreatorTrustEntry> Entries { get; set; } = [];
        public Dictionary<string, CreatorKeyMetadata> MetadataCache { get; set; } = [];
    }
}
