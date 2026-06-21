using System.Text.Json;
using System.Text.Json.Serialization;

namespace Spectralis.Core.Playlists;

/// <summary>
/// Per-playlist JSON files under %LOCALAPPDATA%\Spectralis\playlists — the same
/// folder the legacy app used, so existing playlists carry over unchanged.
/// </summary>
public static class PlaylistStore
{
    private static readonly string Dir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Spectralis", "playlists");

    private static readonly JsonSerializerOptions Opts = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public static void EnsureDirectory() => Directory.CreateDirectory(Dir);

    public static List<Playlist> LoadAll()
    {
        EnsureDirectory();
        var list = new List<Playlist>();
        foreach (var file in Directory.EnumerateFiles(Dir, "*.json"))
        {
            if (file.EndsWith(".smart.json", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            try
            {
                var playlist = JsonSerializer.Deserialize<Playlist>(File.ReadAllText(file), Opts);
                if (playlist is not null)
                {
                    list.Add(playlist);
                }
            }
            catch
            {
            }
        }

        return list.OrderBy(p => p.CreatedAt).ToList();
    }

    public static void Save(Playlist playlist)
    {
        EnsureDirectory();
        var path = Path.Combine(Dir, $"{playlist.Id:N}.json");
        File.WriteAllText(path, JsonSerializer.Serialize(playlist, Opts));
    }

    public static void Delete(Guid id)
    {
        var path = Path.Combine(Dir, $"{id:N}.json");
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    public static List<SmartPlaylist> LoadAllSmart()
    {
        EnsureDirectory();
        var list = new List<SmartPlaylist>();
        foreach (var file in Directory.EnumerateFiles(Dir, "*.smart.json"))
        {
            try
            {
                var playlist = JsonSerializer.Deserialize<SmartPlaylist>(File.ReadAllText(file), Opts);
                if (playlist is not null)
                {
                    list.Add(playlist);
                }
            }
            catch
            {
            }
        }

        return list.OrderBy(p => p.CreatedAt).ToList();
    }

    public static void SaveSmart(SmartPlaylist playlist)
    {
        EnsureDirectory();
        var path = Path.Combine(Dir, $"{playlist.Id:N}.smart.json");
        File.WriteAllText(path, JsonSerializer.Serialize(playlist, Opts));
    }

    public static void DeleteSmart(Guid id)
    {
        var path = Path.Combine(Dir, $"{id:N}.smart.json");
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }
}
