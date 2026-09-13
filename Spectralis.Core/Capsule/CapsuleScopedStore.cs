using System.Text.Json.Nodes;

namespace Spectralis.Core.Capsule;

/// <summary>
/// File-backed per-world/per-capsule key-value store, keyed by a <c>storeKey</c> (the
/// capsule/world id) under <c>%LocalAppData%\Spectralis\capsule-store\{storeKey}.json</c>.
/// Both the HTML bridge (<c>spectral.store.*</c> in <see cref="Web.WebViewHostService"/>) and
/// the Wasm host (<c>storage_get</c>/<c>storage_set</c> imports) share one instance keyed by the
/// same world id, so hand-off state (position, achievements, active DSP preset, ...) round-trips
/// a runtime switch instead of resetting.
/// </summary>
public sealed class CapsuleScopedStore
{
    public const int MaxEntries = 1000;
    public const int MaxKeyBytes = 256;
    public const int MaxValueBytes = 65536;

    private readonly string _filePath;
    private Dictionary<string, JsonNode?>? _store;

    public CapsuleScopedStore(string storeKey)
    {
        var safe = SanitizeFileName(storeKey);
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Spectralis", "capsule-store");
        _filePath = Path.Combine(dir, safe + ".json");
    }

    public JsonNode? Get(string key)
    {
        if (key.Length > MaxKeyBytes)
        {
            return null;
        }

        EnsureLoaded();
        return _store!.TryGetValue(key, out var node) ? node : null;
    }

    /// <summary>Returns false (no-op) if the key is oversized or the store is full and this would add a new entry.</summary>
    public bool Set(string key, JsonNode? value)
    {
        if (key.Length > MaxKeyBytes)
        {
            return false;
        }

        EnsureLoaded();
        if (_store!.Count >= MaxEntries && !_store.ContainsKey(key))
        {
            return false;
        }

        _store[key] = value;
        Save();
        return true;
    }

    public bool Remove(string key)
    {
        if (key.Length > MaxKeyBytes)
        {
            return false;
        }

        EnsureLoaded();
        if (!_store!.Remove(key))
        {
            return false;
        }

        Save();
        return true;
    }

    public void Clear()
    {
        EnsureLoaded();
        _store!.Clear();
        Save();
    }

    private void EnsureLoaded()
    {
        if (_store is not null)
        {
            return;
        }

        _store = [];
        if (!File.Exists(_filePath))
        {
            return;
        }

        try
        {
            var json = File.ReadAllText(_filePath);
            if (JsonNode.Parse(json) is JsonObject obj)
            {
                foreach (var kv in obj)
                {
                    _store[kv.Key] = kv.Value;
                }
            }
        }
        catch
        {
            _store = [];
        }
    }

    private void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
            var obj = new JsonObject();
            foreach (var kv in _store!)
            {
                obj[kv.Key] = kv.Value is null ? null : JsonNode.Parse(kv.Value.ToJsonString());
            }

            File.WriteAllText(_filePath, obj.ToJsonString());
        }
        catch
        {
            // Store write failure is non-fatal.
        }
    }

    private static string SanitizeFileName(string key)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var safe = new string(key.Select(c => invalid.Contains(c) ? '_' : c).ToArray());
        return safe.Length > 64 ? safe[..64] : safe;
    }
}
