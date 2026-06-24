using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Spectralis;

internal sealed class CreatorTrustStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private static string StorePath =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Spectralis",
            "trusted-creators.json");

    private readonly object gate = new();
    private List<CreatorTrustEntry> entries = [];
    private Dictionary<string, CreatorKeyMetadata> metadataCache = [];

    public IReadOnlyList<CreatorTrustEntry> Entries
    {
        get { lock (gate) return [..entries]; }
    }

    public void Load()
    {
        lock (gate)
        {
            try
            {
                if (!File.Exists(StorePath))
                    return;

                var data = JsonSerializer.Deserialize<TrustStoreData>(File.ReadAllText(StorePath), JsonOptions);
                entries = data?.Entries ?? [];
                metadataCache = data?.MetadataCache ?? [];
            }
            catch
            {
                entries = [];
                metadataCache = [];
            }
        }
    }

    public bool IsTrusted(string fingerprint) =>
        entries.Any(e => string.Equals(e.Fingerprint, fingerprint, StringComparison.OrdinalIgnoreCase));

    public void Trust(string fingerprint, string displayName)
    {
        lock (gate)
        {
            entries.RemoveAll(e => string.Equals(e.Fingerprint, fingerprint, StringComparison.OrdinalIgnoreCase));
            entries.Add(new CreatorTrustEntry(fingerprint, displayName, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, null));
        }

        Save();
    }

    public void Revoke(string fingerprint)
    {
        lock (gate)
            entries.RemoveAll(e => string.Equals(e.Fingerprint, fingerprint, StringComparison.OrdinalIgnoreCase));

        Save();
    }

    public void CacheMetadata(string fingerprint, CreatorKeyMetadata metadata)
    {
        lock (gate)
        {
            metadataCache[fingerprint] = metadata;
            var idx = entries.FindIndex(e => string.Equals(e.Fingerprint, fingerprint, StringComparison.OrdinalIgnoreCase));
            if (idx >= 0)
            {
                var entry = entries[idx];
                entries[idx] = entry with
                {
                    LastValidatedUtc = DateTimeOffset.UtcNow,
                    MetadataCachedAtUtc = DateTimeOffset.UtcNow
                };
            }
        }

        Save();
    }

    public CreatorKeyMetadata? GetCachedMetadata(string fingerprint)
    {
        lock (gate)
            return metadataCache.TryGetValue(fingerprint, out var m) ? m : null;
    }

    private void Save()
    {
        lock (gate)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(StorePath)!);
                var data = new TrustStoreData { Entries = [..entries], MetadataCache = new(metadataCache) };
                File.WriteAllText(StorePath, JsonSerializer.Serialize(data, JsonOptions));
            }
            catch { }
        }
    }

    private sealed class TrustStoreData
    {
        public List<CreatorTrustEntry> Entries { get; set; } = [];
        public Dictionary<string, CreatorKeyMetadata> MetadataCache { get; set; } = [];
    }
}
