using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Spectralis;

internal static class PlaylistStore
{
    private static readonly string Dir = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Spectralis", "playlists");

    private static readonly JsonSerializerOptions Opts = new()
    {
        WriteIndented    = true,
        Converters       = { new JsonStringEnumConverter() },
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public static void EnsureDirectory() =>
        Directory.CreateDirectory(Dir);

    // ── Static playlists ─────────────────────────────────────────────────────

    public static List<Playlist> LoadAll()
    {
        EnsureDirectory();
        var list = new List<Playlist>();
        foreach (var file in Directory.EnumerateFiles(Dir, "*.json"))
        {
            if (file.EndsWith(".smart.json", StringComparison.OrdinalIgnoreCase)) continue;
            try
            {
                var json = File.ReadAllText(file);
                var pl   = JsonSerializer.Deserialize<Playlist>(json, Opts);
                if (pl is not null) list.Add(pl);
            }
            catch { }
        }
        return list.OrderBy(p => p.CreatedAt).ToList();
    }

    public static void Save(Playlist playlist)
    {
        EnsureDirectory();
        var path = System.IO.Path.Combine(Dir, $"{playlist.Id:N}.json");
        File.WriteAllText(path, JsonSerializer.Serialize(playlist, Opts));
    }

    public static void Delete(Guid id)
    {
        var path = System.IO.Path.Combine(Dir, $"{id:N}.json");
        if (File.Exists(path)) File.Delete(path);
    }

    // ── Smart playlists ──────────────────────────────────────────────────────

    public static List<SmartPlaylist> LoadAllSmart()
    {
        EnsureDirectory();
        var list = new List<SmartPlaylist>();
        foreach (var file in Directory.EnumerateFiles(Dir, "*.smart.json"))
        {
            try
            {
                var json = File.ReadAllText(file);
                var pl   = JsonSerializer.Deserialize<SmartPlaylist>(json, Opts);
                if (pl is not null) list.Add(pl);
            }
            catch { }
        }
        return list.OrderBy(p => p.CreatedAt).ToList();
    }

    public static void SaveSmart(SmartPlaylist playlist)
    {
        EnsureDirectory();
        var path = System.IO.Path.Combine(Dir, $"{playlist.Id:N}.smart.json");
        File.WriteAllText(path, JsonSerializer.Serialize(playlist, Opts));
    }

    public static void DeleteSmart(Guid id)
    {
        var path = System.IO.Path.Combine(Dir, $"{id:N}.smart.json");
        if (File.Exists(path)) File.Delete(path);
    }
}
