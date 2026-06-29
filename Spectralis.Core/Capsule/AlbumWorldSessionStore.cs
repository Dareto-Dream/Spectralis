using System.Text.Json;
using System.Text.Json.Serialization;

namespace Spectralis.Core.Capsule;

/// <summary>Per-track play stats within a single album world session.</summary>
public sealed class AlbumTrackStats
{
    [JsonPropertyName("playCount")]
    public int PlayCount { get; set; }

    [JsonPropertyName("playedSeconds")]
    public double PlayedSeconds { get; set; }

    [JsonPropertyName("completed")]
    public bool Completed { get; set; }

    [JsonPropertyName("lastPlayedUtc")]
    public DateTime LastPlayedUtc { get; set; }
}

/// <summary>
/// Persisted state for a single .spectral album world: track play counts, achievements,
/// and sequential level-gate progress.  Stored as session.json beside manifest.json in the
/// world's cache directory.
/// </summary>
public sealed class AlbumWorldSession
{
    [JsonPropertyName("currentTrackId")]
    public string? CurrentTrackId { get; set; }

    [JsonPropertyName("currentPositionSeconds")]
    public double CurrentPositionSeconds { get; set; }

    [JsonPropertyName("trackStats")]
    public Dictionary<string, AlbumTrackStats> TrackStats { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    [JsonPropertyName("unlockedAchievements")]
    public List<string> UnlockedAchievements { get; set; } = [];

    [JsonPropertyName("levelGateProgress")]
    public int LevelGateProgress { get; set; }

    [JsonPropertyName("introCompleted")]
    public bool IntroCompleted { get; set; }

    [JsonPropertyName("lastOpenedUtc")]
    public DateTime LastOpenedUtc { get; set; }
}

/// <summary>
/// Reads and writes AlbumWorldSession alongside the cached world directory.
/// The JS bridge's "saveBookmark" message persists arbitrary key/value state here;
/// the unlock/play-count tracking is called by the runtime layer.
/// </summary>
public static class AlbumWorldSessionStore
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private static string SessionPath(string worldDir) =>
        Path.Combine(worldDir, "session.json");

    public static AlbumWorldSession GetSession(string worldDir)
    {
        var path = SessionPath(worldDir);
        if (!File.Exists(path)) return new AlbumWorldSession { LastOpenedUtc = DateTime.UtcNow };

        try
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<AlbumWorldSession>(json) ?? new AlbumWorldSession();
        }
        catch
        {
            return new AlbumWorldSession { LastOpenedUtc = DateTime.UtcNow };
        }
    }

    public static void SaveSession(string worldDir, AlbumWorldSession session)
    {
        try
        {
            Directory.CreateDirectory(worldDir);
            File.WriteAllText(SessionPath(worldDir), JsonSerializer.Serialize(session, JsonOpts));
        }
        catch { }
    }

    /// <summary>Records a track play and increments the level-gate counter if needed.</summary>
    public static void RecordTrackPlay(string worldDir, string trackId, bool advanceLevelGate = false)
    {
        var session = GetSession(worldDir);
        if (!session.TrackStats.TryGetValue(trackId, out var stats))
        {
            stats = new AlbumTrackStats();
            session.TrackStats[trackId] = stats;
        }
        stats.PlayCount++;
        stats.LastPlayedUtc = DateTime.UtcNow;
        if (advanceLevelGate)
            session.LevelGateProgress = Math.Max(session.LevelGateProgress, session.TrackStats.Count);
        session.LastOpenedUtc = DateTime.UtcNow;
        SaveSession(worldDir, session);
    }

    /// <summary>Unlocks an achievement by ID. No-op if already unlocked.</summary>
    public static bool UnlockAchievement(string worldDir, string achievementId)
    {
        var session = GetSession(worldDir);
        if (session.UnlockedAchievements.Contains(achievementId)) return false;
        session.UnlockedAchievements.Add(achievementId);
        SaveSession(worldDir, session);
        return true;
    }

    /// <summary>Stores an arbitrary key/value bookmark from the JS bridge's "saveBookmark" message.</summary>
    public static void SaveBookmark(string worldDir, string key, string value)
    {
        var session = GetSession(worldDir);
        // Encode bookmarks as synthetic achievement IDs: "bookmark:{key}={value}"
        session.UnlockedAchievements.RemoveAll(s => s.StartsWith($"bookmark:{key}=", StringComparison.Ordinal));
        session.UnlockedAchievements.Add($"bookmark:{key}={value}");
        SaveSession(worldDir, session);
    }

    /// <summary>Retrieves a bookmark value previously saved with <see cref="SaveBookmark"/>.</summary>
    public static string? GetBookmark(string worldDir, string key)
    {
        var session = GetSession(worldDir);
        var prefix = $"bookmark:{key}=";
        var entry = session.UnlockedAchievements.LastOrDefault(s => s.StartsWith(prefix, StringComparison.Ordinal));
        return entry is null ? null : entry[prefix.Length..];
    }
}
