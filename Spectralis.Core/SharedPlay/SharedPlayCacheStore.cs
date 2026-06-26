using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using Spectralis.Core.Common;
using Spectralis.Core.Lyrics;

namespace Spectralis.Core.SharedPlay;

public sealed class SharedPlayCacheStore
{
    private static readonly TimeSpan CacheRetention = TimeSpan.FromDays(7);
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    private readonly string cacheRoot;

    public SharedPlayCacheStore()
        : this(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Spectralis",
            "SharedPlay",
            "Cache"))
    {
    }

    internal SharedPlayCacheStore(string cacheRoot)
    {
        this.cacheRoot = Path.GetFullPath(cacheRoot);
    }

    public Task<SharedPlayPackage> CreateOrGetPackageAsync(TrackInfo track, CancellationToken cancellationToken) =>
        Task.Run(() => CreateOrGetPackage(track, cancellationToken), cancellationToken);

    public Task<SharedPlayPackage> CreateOrGetPackageAsync(string path, CancellationToken cancellationToken) =>
        Task.Run(() => CreateOrGetPackage(BuildTrackInfo(path), cancellationToken), cancellationToken);

    public void CleanupExpired()
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
                if (info.LastWriteTimeUtc >= cutoffUtc)
                    continue;

                info.Delete(recursive: true);
            }
        }
        catch
        {
            // Cache cleanup should never block playback.
        }
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
            // Cache cleanup should never block playback shutdown.
        }
    }

    private SharedPlayPackage CreateOrGetPackage(TrackInfo track, CancellationToken cancellationToken)
    {
        EnsureCacheRoot();
        CleanupExpired();

        var sourcePath = Path.GetFullPath(track.SourcePath);
        if (!File.Exists(sourcePath))
            throw new FileNotFoundException("Shared Play could not find the current track file.", sourcePath);

        var sourceInfo = new FileInfo(sourcePath);
        if (sourceInfo.Length <= 0)
            throw new InvalidOperationException("Shared Play cannot upload an empty audio file.");

        if (sourceInfo.Length > SharedPlayDefaults.MaxPackageBytes)
            throw new InvalidOperationException("Shared Play skipped this track because it is larger than the upload safety limit.");

        var audioSha256 = ComputeSha256(sourcePath, cancellationToken);
        var trackId = $"sha256:{audioSha256}";
        var packageDirectory = Path.Combine(cacheRoot, audioSha256);
        Directory.CreateDirectory(packageDirectory);

        var extension = NormalizeExtension(Path.GetExtension(sourcePath));
        var packagePath = Path.Combine(packageDirectory, "spectralis-rich.zip");
        if (!File.Exists(packagePath))
        {
            CreatePackage(track, sourcePath, sourceInfo.Length, audioSha256, trackId, extension, packagePath, cancellationToken);
        }

        var packageInfo = new FileInfo(packagePath);
        if (packageInfo.Length > SharedPlayDefaults.MaxPackageBytes)
            throw new InvalidOperationException("Shared Play skipped this track because its rich package is larger than the upload safety limit.");

        var packageSha256 = ComputeSha256(packagePath, cancellationToken);
        return new SharedPlayPackage(
            trackId,
            packagePath,
            audioSha256,
            packageSha256,
            sourceInfo.Length,
            packageInfo.Length,
            extension,
            CreateTrackDescriptor(track),
            packageInfo.CreationTimeUtc <= DateTime.MinValue
                ? DateTimeOffset.UtcNow
                : new DateTimeOffset(packageInfo.CreationTimeUtc, TimeSpan.Zero));
    }

    private void CreatePackage(
        TrackInfo track,
        string sourcePath,
        long sourceBytes,
        string audioSha256,
        string trackId,
        string extension,
        string packagePath,
        CancellationToken cancellationToken)
    {
        var fullPackagePath = Path.GetFullPath(packagePath);
        if (!IsUnderCacheRoot(fullPackagePath))
            throw new InvalidOperationException("Shared Play refused to write outside its cache directory.");

        var tempPath = Path.Combine(
            Path.GetDirectoryName(fullPackagePath) ?? cacheRoot,
            $"{Guid.NewGuid():N}.tmp");

        try
        {
            using (var archiveStream = new FileStream(tempPath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None))
            using (var archive = new ZipArchive(archiveStream, ZipArchiveMode.Create))
            {
                var manifestEntry = archive.CreateEntry("manifest.json", CompressionLevel.Fastest);
                using (var manifestStream = manifestEntry.Open())
                {
                    var manifest = new
                    {
                        protocolVersion = SharedPlayDefaults.ProtocolVersion,
                        packageKind = "spectralis-rich",
                        createdAtUtc = DateTimeOffset.UtcNow,
                        trackId,
                        audio = new
                        {
                            sha256 = audioSha256,
                            bytes = sourceBytes,
                            extension,
                            entry = $"audio/track{extension}"
                        },
                        track = CreateTrackDescriptor(track),
                        capabilities = CreateCapabilities()
                    };

                    JsonSerializer.Serialize(manifestStream, manifest, JsonOptions);
                }

                var audioEntry = archive.CreateEntry($"audio/track{extension}", CompressionLevel.NoCompression);
                using (var audioStream = audioEntry.Open())
                using (var sourceStream = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    CopyToWithCancellation(sourceStream, audioStream, cancellationToken);
                }

                var sidecarLyricsPath = Path.ChangeExtension(sourcePath, ".lrc");
                if (File.Exists(sidecarLyricsPath))
                {
                    var lyricsEntry = archive.CreateEntry("audio/track.lrc", CompressionLevel.Fastest);
                    using (var lyricsStream = lyricsEntry.Open())
                    using (var sourceLyricsStream = new FileStream(sidecarLyricsPath, FileMode.Open, FileAccess.Read, FileShare.Read))
                    {
                        CopyToWithCancellation(sourceLyricsStream, lyricsStream, cancellationToken);
                    }
                }
            }

            File.Move(tempPath, fullPackagePath, overwrite: true);
            File.SetAttributes(fullPackagePath, FileAttributes.Normal);
        }
        finally
        {
            TryDeleteTempFile(tempPath);
        }
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
            // Hidden is best effort; the user profile ACL remains the main local boundary.
        }
    }

    private bool IsUnderCacheRoot(string candidatePath)
    {
        var normalizedRoot = cacheRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        var normalizedCandidate = Path.GetFullPath(candidatePath);
        return normalizedCandidate.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase);
    }

    private static string ComputeSha256(string path, CancellationToken cancellationToken)
    {
        using var sha256 = SHA256.Create();
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        var buffer = new byte[1024 * 128];
        int read;
        while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            sha256.TransformBlock(buffer, 0, read, null, 0);
        }

        sha256.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
        return Convert.ToHexString(sha256.Hash ?? Array.Empty<byte>()).ToLowerInvariant();
    }

    private static void CopyToWithCancellation(Stream source, Stream destination, CancellationToken cancellationToken)
    {
        var buffer = new byte[1024 * 128];
        int read;
        while ((read = source.Read(buffer, 0, buffer.Length)) > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            destination.Write(buffer, 0, read);
        }
    }

    private static string NormalizeExtension(string extension)
    {
        if (string.IsNullOrWhiteSpace(extension))
            return ".bin";

        var normalized = extension.Trim().ToLowerInvariant();
        if (!normalized.StartsWith('.'))
            normalized = $".{normalized}";

        var safeChars = normalized
            .Where(static character => character == '.' || char.IsAsciiLetterOrDigit(character))
            .ToArray();
        return safeChars.Length <= 1 ? ".bin" : new string(safeChars);
    }

    private static SharedPlayTrackDescriptor CreateTrackDescriptor(TrackInfo track)
    {
        var lyrics = LyricsLoader.LoadForTrack(track.SourcePath);
        return new SharedPlayTrackDescriptor(
            track.DisplayTitle,
            track.Artist,
            track.Album,
            Math.Max(0, track.Duration.TotalSeconds),
            track.FormatName,
            track.Channels,
            track.SampleRateHz,
            0,
            track.CoverArt is { Length: > 0 },
            lyrics is not null,
            track.EmbeddedVisualizer is not null,
            track.EmbeddedTheme is not null,
            track.HasEmbeddedContent,
            BuildSharedPlayLyrics(lyrics));
    }

    private static SharedPlayLyricLine[] BuildSharedPlayLyrics(LyricsDocument? lyrics)
    {
        if (lyrics is null || lyrics.IsDescription || !lyrics.HasLines)
            return Array.Empty<SharedPlayLyricLine>();

        return lyrics.Lines
            .Where(static line => !string.IsNullOrWhiteSpace(line.Text))
            .Take(400)
            .Select(static line => new SharedPlayLyricLine(
                Math.Max(0, line.StartTime),
                line.Text.Trim()))
            .ToArray();
    }

    private static TrackInfo BuildTrackInfo(string path)
    {
        var title = Path.GetFileNameWithoutExtension(path);
        var formatName = Path.GetExtension(path).TrimStart('.').ToUpperInvariant();
        var channels = 2;
        var sampleRate = 44100;
        var duration = TimeSpan.Zero;

        try
        {
            using var file = TagLib.File.Create(path);
            if (!string.IsNullOrWhiteSpace(file.Tag.Title))
                title = file.Tag.Title;
            channels = Math.Max(1, file.Properties.AudioChannels);
            sampleRate = Math.Max(0, file.Properties.AudioSampleRate);
            duration = file.Properties.Duration;
            if (!string.IsNullOrWhiteSpace(file.Properties.Description))
                formatName = file.Properties.Description;
        }
        catch
        {
        }

        return new TrackInfo
        {
            SourcePath = path,
            Title = title,
            FormatName = string.IsNullOrWhiteSpace(formatName) ? "Audio" : formatName,
            Channels = channels,
            SampleRateHz = sampleRate,
            Duration = duration
        };
    }

    internal static SharedPlayCapabilityDescriptor CreateCapabilities() =>
        new(
            SpectralisRichPackage: true,
            PreservesEmbeddedMetadata: true,
            PreservesAlbumArt: true,
            PreservesEmbeddedVisualizer: true,
            BrowserFallbackIncluded: false);

    private static void TryDeleteTempFile(string tempPath)
    {
        try
        {
            if (File.Exists(tempPath))
                File.Delete(tempPath);
        }
        catch
        {
        }
    }
}
