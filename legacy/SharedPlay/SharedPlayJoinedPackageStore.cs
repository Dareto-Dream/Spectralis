using System.IO.Compression;

namespace Spectralis;

internal sealed class SharedPlayJoinedPackageStore
{
    private static readonly TimeSpan CacheRetention = TimeSpan.FromDays(7);

    private readonly string cacheRoot;

    public SharedPlayJoinedPackageStore()
        : this(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Spectralis",
            "SharedPlay",
            "Joined"))
    {
    }

    internal SharedPlayJoinedPackageStore(string cacheRoot)
    {
        this.cacheRoot = Path.GetFullPath(cacheRoot);
    }

    public async Task<string> GetOrDownloadAudioAsync(
        SharedPlayRemoteSession session,
        SharedPlayCdnClient cdnClient,
        CancellationToken cancellationToken)
    {
        EnsureCacheRoot();
        CleanupExpired();

        var sessionDirectory = GetSessionDirectory(session.SessionId, session.TrackId);
        Directory.CreateDirectory(sessionDirectory);

        var audioDirectory = Path.Combine(sessionDirectory, "audio");
        Directory.CreateDirectory(audioDirectory);

        var existingAudio = Directory
            .EnumerateFiles(audioDirectory, "track.*", SearchOption.TopDirectoryOnly)
            .Where(static path => !path.EndsWith(".lrc", StringComparison.OrdinalIgnoreCase))
            .FirstOrDefault(File.Exists);
        if (existingAudio is not null)
        {
            var existingPackagePath = Path.Combine(sessionDirectory, "spectralis-rich.zip");
            if (File.Exists(existingPackagePath) && !File.Exists(Path.Combine(audioDirectory, "track.lrc")))
            {
                using var archive = ZipFile.OpenRead(existingPackagePath);
                await ExtractLyricsSidecarAsync(archive, audioDirectory, cancellationToken);
            }

            return existingAudio;
        }

        var packagePath = Path.Combine(sessionDirectory, "spectralis-rich.zip");
        if (!File.Exists(packagePath))
        {
            var tempPackagePath = Path.Combine(sessionDirectory, $"{Guid.NewGuid():N}.tmp");
            try
            {
                await cdnClient.DownloadPackageAsync(session.PackageUrl, tempPackagePath, cancellationToken);
                File.Move(tempPackagePath, packagePath, overwrite: true);
            }
            finally
            {
                TryDeleteFile(tempPackagePath);
            }
        }

        return await ExtractAudioEntryAsync(packagePath, audioDirectory, cancellationToken);
    }

    public void Clear()
    {
        try
        {
            if (!Directory.Exists(cacheRoot))
                return;

            foreach (var directory in Directory.EnumerateDirectories(cacheRoot))
            {
                var fullPath = Path.GetFullPath(directory);
                if (!IsUnderCacheRoot(fullPath))
                    continue;

                Directory.Delete(fullPath, recursive: true);
            }
        }
        catch
        {
            // Receiver cache cleanup should never interrupt playback shutdown.
        }
    }

    private async Task<string> ExtractAudioEntryAsync(
        string packagePath,
        string audioDirectory,
        CancellationToken cancellationToken)
    {
        using var archive = ZipFile.OpenRead(packagePath);
        var entry = archive.Entries.FirstOrDefault(static candidate =>
            candidate.FullName.StartsWith("audio/track.", StringComparison.OrdinalIgnoreCase) &&
            !candidate.FullName.EndsWith(".lrc", StringComparison.OrdinalIgnoreCase) &&
            !candidate.FullName.EndsWith("/", StringComparison.Ordinal));

        if (entry is null)
            throw new InvalidOperationException("Shared Play package does not contain an audio track.");

        if (entry.Length > SharedPlayDefaults.MaxPackageBytes)
            throw new InvalidOperationException("Shared Play audio is larger than the playback safety limit.");

        var extension = NormalizeExtension(Path.GetExtension(entry.FullName));
        var audioPath = Path.Combine(audioDirectory, $"track{extension}");
        var tempAudioPath = Path.Combine(audioDirectory, $"{Guid.NewGuid():N}.tmp");

        try
        {
            await using (var input = entry.Open())
            await using (var output = new FileStream(tempAudioPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                await input.CopyToAsync(output, cancellationToken);
            }

            File.Move(tempAudioPath, audioPath, overwrite: true);
            await ExtractLyricsSidecarAsync(archive, audioDirectory, cancellationToken);
            return audioPath;
        }
        finally
        {
            TryDeleteFile(tempAudioPath);
        }
    }

    private static async Task ExtractLyricsSidecarAsync(
        ZipArchive archive,
        string audioDirectory,
        CancellationToken cancellationToken)
    {
        var lyricsEntry = archive.GetEntry("audio/track.lrc");
        if (lyricsEntry is null || lyricsEntry.Length <= 0)
            return;

        var lyricsPath = Path.Combine(audioDirectory, "track.lrc");
        var tempLyricsPath = Path.Combine(audioDirectory, $"{Guid.NewGuid():N}.tmp");

        try
        {
            await using (var input = lyricsEntry.Open())
            await using (var output = new FileStream(tempLyricsPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                await input.CopyToAsync(output, cancellationToken);
            }

            File.Move(tempLyricsPath, lyricsPath, overwrite: true);
        }
        finally
        {
            TryDeleteFile(tempLyricsPath);
        }
    }

    private string GetSessionDirectory(string sessionId, string? trackId)
    {
        var safeSessionId = NormalizeSessionId(sessionId);
        if (string.IsNullOrWhiteSpace(safeSessionId))
            safeSessionId = Guid.NewGuid().ToString("N");
        var safeTrackId = NormalizeSessionId(trackId ?? "");

        var sessionDirectory = string.IsNullOrWhiteSpace(safeTrackId)
            ? Path.GetFullPath(Path.Combine(cacheRoot, safeSessionId))
            : Path.GetFullPath(Path.Combine(cacheRoot, safeSessionId, safeTrackId));
        if (!IsUnderCacheRoot(sessionDirectory))
            throw new InvalidOperationException("Shared Play refused to write outside its receiver cache.");

        return sessionDirectory;
    }

    private void EnsureCacheRoot()
    {
        Directory.CreateDirectory(cacheRoot);

        try
        {
            File.SetAttributes(cacheRoot, File.GetAttributes(cacheRoot) | FileAttributes.Hidden);
        }
        catch
        {
        }
    }

    private void CleanupExpired()
    {
        try
        {
            if (!Directory.Exists(cacheRoot))
                return;

            var cutoffUtc = DateTime.UtcNow.Subtract(CacheRetention);
            foreach (var directory in Directory.EnumerateDirectories(cacheRoot))
            {
                var fullPath = Path.GetFullPath(directory);
                if (!IsUnderCacheRoot(fullPath))
                    continue;

                var info = new DirectoryInfo(fullPath);
                if (info.LastWriteTimeUtc < cutoffUtc)
                    info.Delete(recursive: true);
            }
        }
        catch
        {
        }
    }

    private bool IsUnderCacheRoot(string candidatePath)
    {
        var normalizedRoot = cacheRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        var normalizedCandidate = Path.GetFullPath(candidatePath);
        return normalizedCandidate.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeSessionId(string value)
    {
        var chars = value
            .Trim()
            .Where(static character =>
                char.IsAsciiLetterOrDigit(character) ||
                character is '.' or '_' or '-')
            .Take(96)
            .ToArray();

        return new string(chars);
    }

    private static string NormalizeExtension(string extension)
    {
        if (string.IsNullOrWhiteSpace(extension))
            return ".bin";

        var normalized = extension.Trim().ToLowerInvariant();
        if (!normalized.StartsWith('.'))
            normalized = $".{normalized}";

        var chars = normalized
            .Where(static character => character == '.' || char.IsAsciiLetterOrDigit(character))
            .Take(16)
            .ToArray();

        return chars.Length <= 1 ? ".bin" : new string(chars);
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
        }
    }
}
