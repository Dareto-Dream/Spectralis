using System.Text.Json;

namespace Spectralis.Core.Capsule;

/// <summary>
/// Manages the 30-day on-disk cache for unpacked .spectral album world packages.
/// Layout: %LocalAppData%\Spectralis\AlbumWorlds\{fingerprint}\
/// The manifest.json mtime is used as the "last accessed" timestamp for eviction.
/// </summary>
public static class AlbumWorldCacheStore
{
    private static readonly TimeSpan Ttl = TimeSpan.FromDays(30);

    public static string BaseDir { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Spectralis", "AlbumWorlds");

    public static string WorldDir(string fingerprint) =>
        Path.Combine(BaseDir, SanitizeFingerprint(fingerprint));

    /// <summary>
    /// Checks whether a valid, non-expired cached world exists for <paramref name="fingerprint"/>.
    /// Evicts the directory when the cache has expired.
    /// </summary>
    public static bool TryGetCached(string fingerprint, out string worldDir)
    {
        worldDir = WorldDir(fingerprint);
        if (!Directory.Exists(worldDir)) return false;

        var manifestPath = Path.Combine(worldDir, "manifest.json");
        if (!File.Exists(manifestPath))
        {
            TryEvict(worldDir);
            return false;
        }

        if (DateTime.UtcNow - File.GetLastWriteTimeUtc(manifestPath) > Ttl)
        {
            TryEvict(worldDir);
            return false;
        }

        return true;
    }

    /// <summary>Creates (or returns) the world directory for the given fingerprint.</summary>
    public static string PrepareWorldDir(string fingerprint)
    {
        var worldDir = WorldDir(fingerprint);
        Directory.CreateDirectory(worldDir);
        return worldDir;
    }

    /// <summary>Touches manifest.json mtime to reset the 30-day TTL clock.</summary>
    public static void Touch(string worldDir)
    {
        var manifestPath = Path.Combine(worldDir, "manifest.json");
        if (File.Exists(manifestPath))
            File.SetLastWriteTimeUtc(manifestPath, DateTime.UtcNow);
    }

    /// <summary>Writes a manifest file into the world directory and touches its mtime.</summary>
    public static void WriteManifest(string worldDir, object manifestObject)
    {
        var manifestPath = Path.Combine(worldDir, "manifest.json");
        File.WriteAllText(manifestPath, JsonSerializer.Serialize(manifestObject,
            new JsonSerializerOptions { WriteIndented = true }));
    }

    public static void TryEvict(string worldDir)
    {
        try
        {
            if (Directory.Exists(worldDir))
                Directory.Delete(worldDir, recursive: true);
        }
        catch { }
    }

    /// <summary>Evicts all cached worlds (used by Help → Clear Cached Album State).</summary>
    public static void EvictAll()
    {
        if (!Directory.Exists(BaseDir)) return;
        foreach (var dir in Directory.GetDirectories(BaseDir))
            TryEvict(dir);
    }

    /// <summary>Returns the total disk usage of all cached worlds in bytes.</summary>
    public static long GetCacheSizeBytes()
    {
        if (!Directory.Exists(BaseDir)) return 0;
        return Directory
            .EnumerateFiles(BaseDir, "*", SearchOption.AllDirectories)
            .Sum(f =>
            {
                try { return new FileInfo(f).Length; }
                catch { return 0L; }
            });
    }

    private static string SanitizeFingerprint(string fingerprint) =>
        string.Concat(fingerprint.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
}
