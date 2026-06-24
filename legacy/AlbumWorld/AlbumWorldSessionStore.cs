using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Spectralis;

internal sealed class AlbumWorldSession
{
    [JsonPropertyName("albumId")] public string AlbumId { get; set; } = "";
    [JsonPropertyName("currentTrackId")] public string? CurrentTrackId { get; set; }
    [JsonPropertyName("currentPositionSeconds")] public double CurrentPositionSeconds { get; set; }
    [JsonPropertyName("lastPlayedUtc")] public DateTimeOffset LastPlayedUtc { get; set; }
    [JsonPropertyName("trackStats")] public Dictionary<string, AlbumTrackStats> TrackStats { get; set; } = [];
    [JsonPropertyName("bookmarks")] public List<AlbumBookmark> Bookmarks { get; set; } = [];
    [JsonPropertyName("introCompleted")] public bool IntroCompleted { get; set; }
}

internal sealed class AlbumTrackStats
{
    [JsonPropertyName("playedSeconds")] public double PlayedSeconds { get; set; }
    [JsonPropertyName("completed")] public bool Completed { get; set; }
    [JsonPropertyName("lastPlayedUtc")] public DateTimeOffset? LastPlayedUtc { get; set; }
}

internal sealed class AlbumBookmark
{
    [JsonPropertyName("trackId")] public string TrackId { get; set; } = "";
    [JsonPropertyName("positionSeconds")] public double PositionSeconds { get; set; }
    [JsonPropertyName("label")] public string Label { get; set; } = "";
    [JsonPropertyName("createdUtc")] public DateTimeOffset CreatedUtc { get; set; }
}

internal static class AlbumWorldSessionStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    public static AlbumWorldSession Load(string albumDir)
    {
        var path = SessionPath(albumDir);
        if (!File.Exists(path))
            return new AlbumWorldSession();

        try
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<AlbumWorldSession>(json, JsonOptions) ?? new AlbumWorldSession();
        }
        catch
        {
            return new AlbumWorldSession();
        }
    }

    public static void Save(string albumDir, AlbumWorldSession session)
    {
        try
        {
            Directory.CreateDirectory(albumDir);
            var json = JsonSerializer.Serialize(session, JsonOptions);
            File.WriteAllText(SessionPath(albumDir), json);
        }
        catch { }
    }

    public static void Delete(string albumDir)
    {
        try
        {
            var path = SessionPath(albumDir);
            if (File.Exists(path))
                File.Delete(path);
        }
        catch { }
    }

    private static string SessionPath(string albumDir) => Path.Combine(albumDir, "session.json");
}
