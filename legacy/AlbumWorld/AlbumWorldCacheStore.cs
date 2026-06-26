using System.IO;
using System.IO.Compression;
using System.Text.Json;

namespace Spectralis;

internal sealed class AlbumWorldCacheStore
{
    private static readonly TimeSpan CacheRetention = TimeSpan.FromDays(30);
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    private readonly string cacheRoot;
    private bool cleanupRanThisSession;

    public AlbumWorldCacheStore()
        : this(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Spectralis",
            "AlbumWorlds"))
    {
    }

    internal AlbumWorldCacheStore(string cacheRoot)
    {
        this.cacheRoot = Path.GetFullPath(cacheRoot);
    }

    public string GetOrExtract(AlbumCapsulePackage package)
    {
        EnsureCacheRoot();
        if (!cleanupRanThisSession)
        {
            cleanupRanThisSession = true;
            CleanupExpired();
        }

        var albumId = SanitizeId(package.Manifest.Id);
        var albumDir = Path.Combine(cacheRoot, albumId);
        var metaPath = Path.Combine(albumDir, "_meta.json");

        var shouldExtract = ShouldExtract(package, metaPath);
        if (shouldExtract)
        {
            SpectralisLog.Info($"[.spectral] Extracting album cache — id={package.Manifest.Id} payload={package.PayloadSha256[..8]}…");
            var sessionBytes = TryReadSession(albumDir);
            RecreateAlbumDirectory(albumDir);
            Extract(package, albumDir, metaPath);
            TryRestoreSession(albumDir, sessionBytes);
        }
        else
        {
            SpectralisLog.Info($"[.spectral] Using album cache — id={package.Manifest.Id} payload={package.PayloadSha256[..8]}…");
        }

        return albumDir;
    }

    public void TouchAccess(string albumId)
    {
        var metaPath = Path.Combine(cacheRoot, SanitizeId(albumId), "_meta.json");
        if (!File.Exists(metaPath))
            return;

        try
        {
            var meta = ReadMeta(metaPath);
            meta.LastPlayedUtc = DateTimeOffset.UtcNow;
            WriteMeta(metaPath, meta);
        }
        catch { }
    }

    public bool IsPinned(string albumId)
    {
        var metaPath = Path.Combine(cacheRoot, SanitizeId(albumId), "_meta.json");
        if (!File.Exists(metaPath))
            return false;

        try { return ReadMeta(metaPath).Pinned; }
        catch { return false; }
    }

    public void SetPinned(string albumId, bool pinned)
    {
        var metaPath = Path.Combine(cacheRoot, SanitizeId(albumId), "_meta.json");
        if (!File.Exists(metaPath))
            return;

        try
        {
            var meta = ReadMeta(metaPath);
            meta.Pinned = pinned;
            WriteMeta(metaPath, meta);
        }
        catch { }
    }

    public void CleanupExpired()
    {
        try
        {
            if (!Directory.Exists(cacheRoot))
                return;

            var cutoffUtc = DateTimeOffset.UtcNow.Subtract(CacheRetention);
            foreach (var directory in Directory.EnumerateDirectories(cacheRoot))
            {
                var fullPath = Path.GetFullPath(directory);
                if (!IsUnderCacheRoot(fullPath))
                    continue;

                var metaPath = Path.Combine(fullPath, "_meta.json");
                if (!File.Exists(metaPath))
                    continue;

                try
                {
                    var meta = ReadMeta(metaPath);
                    if (meta.Pinned)
                        continue;
                    if (meta.LastPlayedUtc >= cutoffUtc)
                        continue;
                }
                catch { continue; }

                try { Directory.Delete(fullPath, recursive: true); }
                catch { }
            }
        }
        catch { }
    }

    public void DeleteAlbum(string albumId)
    {
        var albumDir = Path.GetFullPath(Path.Combine(cacheRoot, SanitizeId(albumId)));
        if (!IsUnderCacheRoot(albumDir))
            return;

        try
        {
            if (Directory.Exists(albumDir))
                Directory.Delete(albumDir, recursive: true);
        }
        catch { }
    }

    public int ClearAllState()
    {
        if (!Directory.Exists(cacheRoot))
            return 0;

        var cleared = 0;
        foreach (var directory in Directory.EnumerateDirectories(cacheRoot))
        {
            var fullPath = Path.GetFullPath(directory);
            if (!IsUnderCacheRoot(fullPath))
                continue;

            var sessionPath = Path.Combine(fullPath, "session.json");
            try
            {
                if (!File.Exists(sessionPath))
                    continue;

                File.Delete(sessionPath);
                cleared++;
            }
            catch { }
        }

        return cleared;
    }

    private void Extract(AlbumCapsulePackage package, string albumDir, string metaPath)
    {
        Directory.CreateDirectory(albumDir);

        package.ExtractAll(albumDir);

        var meta = new AlbumCacheMeta
        {
            AlbumId = package.Manifest.Id,
            ExtractedAtUtc = DateTimeOffset.UtcNow,
            LastPlayedUtc = DateTimeOffset.UtcNow,
            Pinned = false,
            SourceFingerprint = package.Fingerprint,
            PayloadSha256 = package.PayloadSha256
        };
        WriteMeta(metaPath, meta);
    }

    private bool ShouldExtract(AlbumCapsulePackage package, string metaPath)
    {
        if (!File.Exists(metaPath))
            return true;

        try
        {
            var meta = ReadMeta(metaPath);
            return !string.Equals(meta.AlbumId, package.Manifest.Id, StringComparison.Ordinal) ||
                !string.Equals(meta.SourceFingerprint, package.Fingerprint, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(meta.PayloadSha256, package.PayloadSha256, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return true;
        }
    }

    private void RecreateAlbumDirectory(string albumDir)
    {
        var fullPath = Path.GetFullPath(albumDir);
        if (!IsUnderCacheRoot(fullPath))
            throw new InvalidOperationException("Album cache path is outside the cache root.");

        if (Directory.Exists(fullPath))
            Directory.Delete(fullPath, recursive: true);

        Directory.CreateDirectory(fullPath);
    }

    private static byte[]? TryReadSession(string albumDir)
    {
        try
        {
            var path = Path.Combine(albumDir, "session.json");
            return File.Exists(path) ? File.ReadAllBytes(path) : null;
        }
        catch
        {
            return null;
        }
    }

    private static void TryRestoreSession(string albumDir, byte[]? sessionBytes)
    {
        if (sessionBytes is null)
            return;

        try
        {
            File.WriteAllBytes(Path.Combine(albumDir, "session.json"), sessionBytes);
        }
        catch { }
    }

    private void EnsureCacheRoot()
    {
        Directory.CreateDirectory(cacheRoot);

        try
        {
            File.SetAttributes(cacheRoot, File.GetAttributes(cacheRoot) | FileAttributes.Hidden);
        }
        catch { }
    }

    private bool IsUnderCacheRoot(string candidatePath)
    {
        var normalizedRoot = cacheRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        var normalizedCandidate = Path.GetFullPath(candidatePath);
        return normalizedCandidate.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase);
    }

    private static AlbumCacheMeta ReadMeta(string metaPath)
    {
        var json = File.ReadAllText(metaPath);
        return JsonSerializer.Deserialize<AlbumCacheMeta>(json, JsonOptions) ?? new AlbumCacheMeta();
    }

    private static void WriteMeta(string metaPath, AlbumCacheMeta meta)
    {
        var json = JsonSerializer.Serialize(meta, JsonOptions);
        File.WriteAllText(metaPath, json);
    }

    private static string SanitizeId(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
            return "unknown";

        var safe = new string(id.Where(c =>
            char.IsAsciiLetterOrDigit(c) || c == '-' || c == '_' || c == '.').ToArray());

        return string.IsNullOrEmpty(safe) ? "unknown" : safe[..Math.Min(safe.Length, 64)];
    }
}

internal sealed class AlbumCacheMeta
{
    [System.Text.Json.Serialization.JsonPropertyName("albumId")]
    public string AlbumId { get; set; } = "";

    [System.Text.Json.Serialization.JsonPropertyName("extractedAtUtc")]
    public DateTimeOffset ExtractedAtUtc { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("lastPlayedUtc")]
    public DateTimeOffset LastPlayedUtc { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("pinned")]
    public bool Pinned { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("sourceFingerprint")]
    public string SourceFingerprint { get; set; } = "";

    [System.Text.Json.Serialization.JsonPropertyName("payloadSha256")]
    public string PayloadSha256 { get; set; } = "";
}
