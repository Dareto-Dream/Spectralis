using System.IO;
using System.Text.Json;

namespace Spectralis;

/// <summary>
/// Persists per-track content-warning tags to
/// %LocalAppData%\Spectralis\content_warnings.json.
/// </summary>
internal static class TrackContentWarningStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
    };

    private static string StorePath =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Spectralis",
            "content_warnings.json");

    // In-memory cache; invalidated on every write.
    private static Dictionary<string, string[]>? _cache;

    public static string[] Get(string filePath)
    {
        var data = EnsureLoaded();
        return data.TryGetValue(NormalizePath(filePath), out var tags) ? tags : [];
    }

    public static bool HasWarnings(string filePath) => Get(filePath).Length > 0;

    public static void Set(string filePath, string[] tags)
    {
        var data = EnsureLoaded();
        var key = NormalizePath(filePath);

        var cleaned = tags
            .Select(static t => t.Trim())
            .Where(static t => !string.IsNullOrEmpty(t))
            .ToArray();

        if (cleaned.Length == 0)
            data.Remove(key);
        else
            data[key] = cleaned;

        _cache = data;
        Persist(data);
    }

    public static void Clear(string filePath)
    {
        var data = EnsureLoaded();
        if (data.Remove(NormalizePath(filePath)))
        {
            _cache = data;
            Persist(data);
        }
    }

    // ── internals ─────────────────────────────────────────────────────────

    private static Dictionary<string, string[]> EnsureLoaded()
    {
        if (_cache is not null)
            return _cache;

        try
        {
            if (!File.Exists(StorePath))
                return _cache = [];

            var json = File.ReadAllText(StorePath);
            return _cache = JsonSerializer.Deserialize<Dictionary<string, string[]>>(json, SerializerOptions) ?? [];
        }
        catch
        {
            return _cache = [];
        }
    }

    private static void Persist(Dictionary<string, string[]> data)
    {
        try
        {
            var dir = Path.GetDirectoryName(StorePath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            File.WriteAllText(StorePath, JsonSerializer.Serialize(data, SerializerOptions));
        }
        catch { }
    }

    private static string NormalizePath(string path)
    {
        try { return Path.GetFullPath(path).ToLowerInvariant(); }
        catch { return path.ToLowerInvariant(); }
    }
}
