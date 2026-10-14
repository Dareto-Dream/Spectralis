using Spectralis.Core.Integrations;
using Spectralis.Core.Common;

namespace Spectralis.App.Services;

internal static class SongDownloadService
{
    private static readonly TimeSpan ConversionTimeout = TimeSpan.FromHours(2);

    public static async Task<string> DownloadFromUrlAsync(
        string sourceUrl,
        string destinationFolder,
        bool formatAsMp3,
        string? title,
        string? artist,
        string? album,
        byte[]? coverArt,
        CancellationToken cancellationToken = default)
    {
        if (!YtDlpService.IsAllowedMediaUrl(sourceUrl))
            throw new InvalidOperationException("This source cannot be downloaded as a file.");

        var ytDlp = YtDlpService.FindExecutable()
            ?? throw new InvalidOperationException("yt-dlp was not found. Add yt-dlp beside Spectralis or put it on PATH to download this source.");
        var stagingFolder = Path.Combine(Path.GetTempPath(), "Spectralis", "song-download", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(stagingFolder);
        try
        {
            var result = await SafeProcessRunner.RunAsync(
                ytDlp,
                [
                    "--no-playlist", "--no-warnings", "--no-progress", "--no-write-info-json",
                    "--no-overwrites", "-f", "bestaudio/best",
                    "-o", Path.Combine(stagingFolder, "%(title)s.%(ext)s"),
                    "--", sourceUrl,
                ],
                ConversionTimeout,
                maxOutputBytes: 128 * 1024,
                cancellationToken: cancellationToken);

            if (!result.Succeeded)
            {
                var reason = result.TimedOut ? "yt-dlp timed out while downloading this source." : result.Stderr.Trim();
                throw new InvalidOperationException(string.IsNullOrWhiteSpace(reason)
                    ? "yt-dlp could not download this source."
                    : $"Could not download this source: {reason}");
            }

            var downloadedPath = Directory.EnumerateFiles(stagingFolder)
                .Where(SupportedAudioFormats.IsSupportedExtension)
                .OrderByDescending(static path => new FileInfo(path).Length)
                .FirstOrDefault(static path => new FileInfo(path).Length > 0)
                ?? throw new InvalidOperationException("yt-dlp finished without producing a supported audio file.");

            return await SaveAsync(downloadedPath, destinationFolder, formatAsMp3,
                title, artist, album, coverArt, cancellationToken);
        }
        finally
        {
            try { Directory.Delete(stagingFolder, recursive: true); } catch { }
        }
    }

    public static async Task<string> SaveAsync(
        string sourcePath,
        string destinationFolder,
        bool formatAsMp3,
        string? title,
        string? artist,
        string? album,
        byte[]? coverArt,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(sourcePath))
            throw new FileNotFoundException("The current audio file is no longer available.", sourcePath);

        Directory.CreateDirectory(destinationFolder);
        var stem = formatAsMp3
            ? BuildStem(artist, title, Path.GetFileNameWithoutExtension(sourcePath))
            : Path.GetFileNameWithoutExtension(sourcePath);
        var extension = formatAsMp3 ? ".mp3" : Path.GetExtension(sourcePath);
        var destinationPath = ReserveUniquePath(destinationFolder, stem, extension);

        try
        {
            if (formatAsMp3)
            {
                await ConvertToMp3Async(sourcePath, destinationPath, cancellationToken);
                WriteMetadata(destinationPath, title, artist, album, coverArt);
            }
            else
            {
                await CopyAsync(sourcePath, destinationPath, cancellationToken);
            }

            return destinationPath;
        }
        catch
        {
            try { File.Delete(destinationPath); } catch { }
            throw;
        }
    }

    private static async Task ConvertToMp3Async(string sourcePath, string destinationPath, CancellationToken cancellationToken)
    {
        var ffmpeg = FfmpegLocator.FindExecutable();
        if (string.IsNullOrWhiteSpace(ffmpeg))
            throw new InvalidOperationException("FFmpeg was not found. Add ffmpeg beside Spectralis or put it on PATH to auto-format downloads.");

        var result = await SafeProcessRunner.RunAsync(
            ffmpeg,
            [
                "-nostdin", "-hide_banner", "-loglevel", "error", "-y",
                "-i", sourcePath,
                "-map", "0:a:0", "-vn",
                "-codec:a", "libmp3lame", "-q:a", "2",
                "-id3v2_version", "3",
                destinationPath,
            ],
            ConversionTimeout,
            maxOutputBytes: 64 * 1024,
            cancellationToken: cancellationToken);

        if (!result.Succeeded || !File.Exists(destinationPath) || new FileInfo(destinationPath).Length == 0)
        {
            var reason = result.TimedOut
                ? "FFmpeg timed out while converting the download."
                : result.Stderr.Trim();
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(reason)
                ? "FFmpeg could not convert this audio file to MP3."
                : $"Could not convert this audio file to MP3: {reason}");
        }
    }

    private static void WriteMetadata(string path, string? title, string? artist, string? album, byte[]? coverArt)
    {
        using var file = TagLib.File.Create(path);
        if (!string.IsNullOrWhiteSpace(title))
            file.Tag.Title = title.Trim();
        if (!string.IsNullOrWhiteSpace(artist))
        {
            file.Tag.Performers = [artist.Trim()];
            file.Tag.AlbumArtists = [artist.Trim()];
        }
        if (!string.IsNullOrWhiteSpace(album))
            file.Tag.Album = album.Trim();
        if (coverArt is { Length: > 0 })
        {
            file.Tag.Pictures =
            [
                new TagLib.Picture(new TagLib.ByteVector(coverArt))
                {
                    Type = TagLib.PictureType.FrontCover,
                    MimeType = DetectImageMimeType(coverArt),
                },
            ];
        }
        file.Save();
    }

    private static async Task CopyAsync(string sourcePath, string destinationPath, CancellationToken cancellationToken)
    {
        await using var source = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read,
            128 * 1024, useAsync: true);
        await using var destination = new FileStream(destinationPath, FileMode.CreateNew, FileAccess.Write, FileShare.None,
            128 * 1024, useAsync: true);
        await source.CopyToAsync(destination, 128 * 1024, cancellationToken);
    }

    private static string ReserveUniquePath(string folder, string stem, string extension)
    {
        var candidate = Path.Combine(folder, stem + extension);
        for (var suffix = 2; File.Exists(candidate); suffix++)
            candidate = Path.Combine(folder, $"{stem} ({suffix}){extension}");
        return candidate;
    }

    private static string BuildStem(string? artist, string? title, string fallback)
    {
        var baseName = string.IsNullOrWhiteSpace(title) ? fallback : title.Trim();
        var combined = string.IsNullOrWhiteSpace(artist) ? baseName : $"{artist.Trim()} - {baseName}";
        var invalid = Path.GetInvalidFileNameChars().Concat("<>:\"/\\|?*".ToCharArray()).ToHashSet();
        var safe = new string(combined.Select(ch => invalid.Contains(ch) || char.IsControl(ch) ? '_' : ch).ToArray())
            .Trim().TrimEnd('.');
        if (safe.Length > 150)
            safe = safe[..150].TrimEnd(' ', '.');
        return string.IsNullOrWhiteSpace(safe) ? "download" : safe;
    }

    private static string DetectImageMimeType(byte[] bytes)
    {
        if (bytes.Length >= 8 && bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47)
            return "image/png";
        if (bytes.Length >= 3 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF)
            return "image/jpeg";
        if (bytes.Length >= 12 && bytes[0] == 'R' && bytes[1] == 'I' && bytes[2] == 'F' && bytes[3] == 'F' &&
            bytes[8] == 'W' && bytes[9] == 'E' && bytes[10] == 'B' && bytes[11] == 'P')
            return "image/webp";
        return "application/octet-stream";
    }
}
