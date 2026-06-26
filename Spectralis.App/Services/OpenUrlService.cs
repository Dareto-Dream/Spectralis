using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Spectralis.Core.Common;
using Spectralis.Core.Integrations;

namespace Spectralis.App.Services;

public enum RemoteAudioServiceKind
{
    DirectAudio,
    Untitled,
    YouTube,
    SoundCloud,
    BandLab,
    Suno,
    Spotify,
    Generic,
}

public sealed record RemoteAudioResolveResult(
    RemoteAudioServiceKind Kind,
    string ServiceLabel,
    string SourceUrl,
    string AudioUrl,
    string DownloadExtension,
    string FormatName,
    string? Title,
    string? Artist,
    string? Album,
    string? ArtworkUrl,
    TimeSpan Duration,
    string? RefererUrl,
    string? CachedAudioPath = null,
    string? ExternalId = null,
    string? LyricsText = null,
    string? WebViewEmbedHtml = null,
    // True when only metadata (title/artist/art) was resolved and the audio itself
    // still needs a full ResolveAsync(quickOnly: false) call to become playable.
    bool MetadataOnly = false);

public static class RemoteAudioResolveResultExtensions
{
    /// <summary>True when this result should be displayed via the embedded WebView widget instead of native audio playback.</summary>
    public static bool IsWebViewFallback(this RemoteAudioResolveResult result) =>
        !string.IsNullOrEmpty(result.WebViewEmbedHtml);
}

public sealed class OpenUrlService
{
    private static readonly Regex YouTubeVideoIdPattern = new("^[A-Za-z0-9_-]{11}$", RegexOptions.Compiled);
    private static readonly HttpClient Http = CreateHttpClient();
    private static readonly TimeSpan YtDlpInfoTimeout = TimeSpan.FromSeconds(60);

    public static string UntitledLogPath => AppLogPaths.For("untitled.log");
    public static string YouTubeLogPath => AppLogPaths.For("youtube.log");

    public async Task<RemoteAudioResolveResult> ResolveAsync(string input, CancellationToken cancellationToken, bool quickOnly = false)
    {
        var sourceUri = NormalizeInput(input);
        var expandedUri = await TryExpandRedirectAsync(sourceUri, cancellationToken);
        var kind = DetectTarget(expandedUri);

        return kind switch
        {
            RemoteAudioServiceKind.DirectAudio => ResolveDirect(expandedUri),
            RemoteAudioServiceKind.Untitled => await ResolveUntitledTrackAsync(expandedUri, cancellationToken),
            RemoteAudioServiceKind.Suno => await ResolveSunoWithFallbackAsync(expandedUri, cancellationToken),
            RemoteAudioServiceKind.SoundCloud => await ResolveSoundCloudWithFallbackAsync(expandedUri, cancellationToken),
            RemoteAudioServiceKind.BandLab => await BandLabNativeResolver.ResolveAsync(expandedUri, cancellationToken),
            RemoteAudioServiceKind.Spotify => BuildSpotifyWebViewResult(expandedUri),
            _ => await ResolveWithYtDlpAsync(expandedUri, kind, cancellationToken, quickOnly),
        };
    }

    public static bool MightBeUrl(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        try
        {
            _ = NormalizeInput(value);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static async Task<byte[]?> TryFetchArtworkBytesAsync(string? artworkUrl, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(artworkUrl) ||
            !Uri.TryCreate(artworkUrl, UriKind.Absolute, out var uri) ||
            uri.Scheme is not ("http" or "https"))
        {
            return null;
        }

        try
        {
            return await Http.GetByteArrayAsync(uri, cancellationToken);
        }
        catch
        {
            return null;
        }
    }

    private static Uri NormalizeInput(string input)
    {
        var trimmed = input.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            throw new InvalidOperationException("Enter a URL to open.");
        }

        if (YouTubeVideoIdPattern.IsMatch(trimmed))
        {
            trimmed = $"https://www.youtube.com/watch?v={trimmed}";
        }
        else if (!trimmed.Contains("://", StringComparison.Ordinal) &&
                 SunoClipResolver.TryExtractClipId(trimmed, out var sunoClipId))
        {
            trimmed = $"https://suno.com/song/{sunoClipId}";
        }
        else if (!trimmed.Contains("://", StringComparison.Ordinal) &&
                 trimmed.Contains('.', StringComparison.Ordinal) &&
                 !trimmed.Contains(' ', StringComparison.Ordinal))
        {
            trimmed = "https://" + trimmed;
        }

        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri) ||
            uri.Scheme is not ("http" or "https"))
        {
            throw new InvalidOperationException("Only http and https media URLs can be opened.");
        }

        return uri;
    }

    /// <summary>Follows HTTP redirects (including short-link services) and returns the final URI. Never throws.</summary>
    public static async Task<Uri> ExpandRedirectAsync(Uri uri, CancellationToken cancellationToken = default)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.Accept.ParseAdd("text/html,application/xhtml+xml,*/*");
            using var response = await Http.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
            return response.RequestMessage?.RequestUri ?? uri;
        }
        catch
        {
            return uri;
        }
    }

    private static Task<Uri> TryExpandRedirectAsync(Uri uri, CancellationToken cancellationToken) =>
        ExpandRedirectAsync(uri, cancellationToken);

    private static async Task<RemoteAudioResolveResult> ResolveSoundCloudWithFallbackAsync(
        Uri uri, CancellationToken cancellationToken)
    {
        try
        {
            return await SoundCloudNativeResolver.ResolveAsync(uri, cancellationToken);
        }
        catch (OperationCanceledException) { throw; }
        catch
        {
            return BuildWidgetFallbackResult(RemoteAudioServiceKind.SoundCloud, "SoundCloud", uri.AbsoluteUri,
                "<!DOCTYPE html><html><head><meta charset=\"utf-8\">" +
                "<style>*{margin:0;padding:0;box-sizing:border-box}html,body{width:100%;height:100%;background:#000;overflow:hidden}</style>" +
                "</head><body>" +
                "<iframe width=\"100%\" height=\"100%\" allow=\"autoplay\"" +
                " src=\"https://w.soundcloud.com/player/?url=" + Uri.EscapeDataString(uri.AbsoluteUri) +
                "&amp;auto_play=true&amp;color=%23ff5500&amp;buying=false&amp;sharing=false&amp;download=false" +
                "&amp;show_artwork=true&amp;show_comments=false&amp;show_user=true&amp;show_reposts=false&amp;show_teaser=false&amp;visual=true\">" +
                "</iframe></body></html>");
        }
    }

    private static async Task<RemoteAudioResolveResult> ResolveSunoWithFallbackAsync(
        Uri uri, CancellationToken cancellationToken)
    {
        try
        {
            return await ResolveSunoTrackAsync(uri, cancellationToken);
        }
        catch (OperationCanceledException) { throw; }
        catch
        {
            return BuildWidgetFallbackResult(RemoteAudioServiceKind.Suno, "Suno", uri.AbsoluteUri,
                "<!DOCTYPE html><html><head><meta charset=\"utf-8\">" +
                "<style>*{margin:0;padding:0;box-sizing:border-box}html,body{width:100%;height:100%;background:#0f0f0f;overflow:hidden;display:flex;align-items:center;justify-content:center}</style>" +
                "</head><body>" +
                "<iframe width=\"100%\" height=\"100%\" style=\"border:none\" allow=\"autoplay\"" +
                " src=\"" + System.Net.WebUtility.HtmlEncode(uri.AbsoluteUri) + "\">" +
                "</iframe></body></html>");
        }
    }

    private static RemoteAudioResolveResult BuildSpotifyWebViewResult(Uri uri)
    {
        var embedUrl = uri.AbsoluteUri
            .Replace("open.spotify.com/", "open.spotify.com/embed/", StringComparison.OrdinalIgnoreCase);
        var html =
            "<!DOCTYPE html><html><head><meta charset=\"utf-8\">" +
            "<style>*{margin:0;padding:0;box-sizing:border-box}html,body{width:100%;height:100%;background:#191414;overflow:hidden}</style>" +
            "</head><body>" +
            "<iframe width=\"100%\" height=\"100%\" style=\"border:none;border-radius:0\" allow=\"autoplay; clipboard-write; encrypted-media; fullscreen; picture-in-picture\" loading=\"lazy\"" +
            " src=\"" + System.Net.WebUtility.HtmlEncode(embedUrl) + "\">" +
            "</iframe></body></html>";
        return BuildWidgetFallbackResult(RemoteAudioServiceKind.Spotify, "Spotify", uri.AbsoluteUri, html);
    }

    private static RemoteAudioResolveResult BuildWidgetFallbackResult(
        RemoteAudioServiceKind kind, string label, string sourceUrl, string html) =>
        new(
            kind,
            label,
            sourceUrl,
            AudioUrl: string.Empty,
            DownloadExtension: string.Empty,
            FormatName: $"{label} widget",
            Title: null,
            Artist: null,
            Album: null,
            ArtworkUrl: null,
            Duration: TimeSpan.Zero,
            RefererUrl: null,
            WebViewEmbedHtml: html);

    private static RemoteAudioServiceKind DetectTarget(Uri uri)
    {
        if (IsDirectAudioUri(uri))
        {
            return RemoteAudioServiceKind.DirectAudio;
        }

        var host = uri.Host.ToLowerInvariant();
        if (host == "untitled.stream" || host.EndsWith(".untitled.stream", StringComparison.Ordinal))
        {
            return RemoteAudioServiceKind.Untitled;
        }

        if (host.Contains("soundcloud.com", StringComparison.Ordinal))
        {
            return RemoteAudioServiceKind.SoundCloud;
        }

        if (host.Contains("youtube.com", StringComparison.Ordinal) ||
            host.Contains("youtu.be", StringComparison.Ordinal))
        {
            return RemoteAudioServiceKind.YouTube;
        }

        if (host.Contains("bandlab.com", StringComparison.Ordinal))
        {
            return RemoteAudioServiceKind.BandLab;
        }

        if (host.Contains("suno.com", StringComparison.Ordinal) ||
            host.Contains("suno.ai", StringComparison.Ordinal))
        {
            return RemoteAudioServiceKind.Suno;
        }

        if (host.Contains("spotify.com", StringComparison.Ordinal) ||
            uri.Scheme.Equals("spotify", StringComparison.OrdinalIgnoreCase))
        {
            return RemoteAudioServiceKind.Spotify;
        }

        return RemoteAudioServiceKind.Generic;
    }

    private static bool IsDirectAudioUri(Uri uri) =>
        SupportedAudioFormats.Extensions.Contains(
            Path.GetExtension(uri.AbsolutePath),
            StringComparer.OrdinalIgnoreCase);

    private static RemoteAudioResolveResult ResolveDirect(Uri uri)
    {
        var extension = NormalizeAudioExtension(Path.GetExtension(uri.AbsolutePath), ".mp3");
        return new RemoteAudioResolveResult(
            RemoteAudioServiceKind.DirectAudio,
            "Remote audio",
            uri.AbsoluteUri,
            uri.AbsoluteUri,
            extension,
            $"{extension.TrimStart('.').ToUpperInvariant()} stream",
            Path.GetFileNameWithoutExtension(Uri.UnescapeDataString(uri.AbsolutePath)),
            null,
            "Remote audio",
            null,
            TimeSpan.Zero,
            uri.AbsoluteUri);
    }

    private async Task<RemoteAudioResolveResult> ResolveWithYtDlpAsync(
        Uri uri,
        RemoteAudioServiceKind kind,
        CancellationToken cancellationToken,
        bool quickOnly = false)
    {
        var isYouTube = kind == RemoteAudioServiceKind.YouTube;
        if (isYouTube)
            AppLogPaths.AppendTimestamped(YouTubeLogPath, $"--- ResolveWithYtDlpAsync: {uri.AbsoluteUri}");

        var executable = YtDlpService.FindExecutable();
        if (string.IsNullOrWhiteSpace(executable))
        {
            if (isYouTube)
                AppLogPaths.AppendTimestamped(YouTubeLogPath, "yt-dlp not found on PATH or app directory");
            throw new InvalidOperationException(
                "yt-dlp was not found. Put yt-dlp.exe beside Spectralis or on PATH to open this service link.");
        }

        if (isYouTube)
            AppLogPaths.AppendTimestamped(YouTubeLogPath, $"yt-dlp: {executable}");

        var info = await TryGetYtDlpInfoAsync(executable, uri.AbsoluteUri, cancellationToken);
        if (quickOnly)
        {
            if (info is null)
            {
                if (isYouTube)
                    AppLogPaths.AppendTimestamped(YouTubeLogPath, "metadata fetch failed");
                throw new InvalidOperationException("Could not read track info for this link.");
            }

            if (isYouTube)
                AppLogPaths.AppendTimestamped(YouTubeLogPath, $"metadata ready  title=\"{info.Title}\" artist=\"{info.Artist}\"");

            return new RemoteAudioResolveResult(
                kind,
                ServiceLabel(kind),
                uri.AbsoluteUri,
                uri.AbsoluteUri,
                "",
                $"{ServiceLabel(kind)} stream",
                info.Title,
                info.Artist,
                info.Album ?? ServiceLabel(kind),
                info.ArtworkUrl,
                info.Duration,
                uri.AbsoluteUri,
                CachedAudioPath: null,
                ExternalId: isYouTube ? ExtractYouTubeVideoId(uri) : null,
                MetadataOnly: true);
        }

        if (isYouTube)
            AppLogPaths.AppendTimestamped(YouTubeLogPath, "downloading audio via yt-dlp");

        var cachedPath = await DownloadWithYtDlpAsync(executable, uri.AbsoluteUri, cancellationToken, _ytDlpProgressCallback);
        var extension = NormalizeAudioExtension(Path.GetExtension(cachedPath), ".m4a");

        if (isYouTube)
            AppLogPaths.AppendTimestamped(YouTubeLogPath, $"stream ready  title=\"{info?.Title}\" artist=\"{info?.Artist}\" dur={info?.Duration}");

        return new RemoteAudioResolveResult(
            kind,
            ServiceLabel(kind),
            uri.AbsoluteUri,
            cachedPath,
            extension,
            $"{ServiceLabel(kind)} stream",
            info?.Title,
            info?.Artist,
            info?.Album ?? ServiceLabel(kind),
            info?.ArtworkUrl,
            info?.Duration ?? TimeSpan.Zero,
            uri.AbsoluteUri,
            cachedPath,
            isYouTube ? ExtractYouTubeVideoId(uri) : null);
    }

    private Action<string>? _ytDlpProgressCallback;

    /// <summary>
    /// Registers a callback that receives live yt-dlp download progress lines (e.g. "[download] 42% of …").
    /// Parse with <see cref="TryParseYtDlpProgress"/> to extract a percentage.
    /// </summary>
    public void SetYtDlpProgressCallback(Action<string>? callback) => _ytDlpProgressCallback = callback;

    /// <summary>
    /// Parses a yt-dlp stdout line like "[download]  42.1% of   5.23MiB" into an integer percentage.
    /// Returns false for non-progress lines.
    /// </summary>
    public static bool TryParseYtDlpProgress(string line, out int percent)
    {
        percent = 0;
        var trimmed = line.TrimStart();
        if (!trimmed.StartsWith("[download]", StringComparison.Ordinal)) return false;
        var after = trimmed[10..].TrimStart();
        var spaceIdx = after.IndexOf('%', StringComparison.Ordinal);
        if (spaceIdx <= 0) return false;
        var numStr = after[..spaceIdx].Trim();
        if (!double.TryParse(numStr, System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out var d)) return false;
        percent = (int)Math.Clamp(d, 0, 100);
        return true;
    }

    private static async Task<string> DownloadWithYtDlpAsync(
        string executable,
        string url,
        CancellationToken cancellationToken,
        Action<string>? onProgressLine = null)
    {
        var outputTemplate = RemoteAudioCache.CreateDownloadTemplate(out var token);
        var result = await SafeProcessRunner.RunWithProgressAsync(
            executable,
            [
                "--no-playlist",
                "--progress",
                "--newline",
                "--force-overwrites",
                "--no-part",
                "-f",
                "bestaudio[ext=m4a]/bestaudio[ext=mp3]/bestaudio[ext=webm]/bestaudio",
                "-o",
                outputTemplate,
                "--",
                url,
            ],
            TimeSpan.FromMinutes(5),
            onStdoutLine: onProgressLine,
            maxOutputBytes: 2 * 1024 * 1024,
            cancellationToken);

        if (result.TimedOut)
        {
            throw new InvalidOperationException("yt-dlp timed out downloading the audio.");
        }

        if (result.ExitCode != 0)
        {
            var stderr = result.Stderr.Trim();
            throw new InvalidOperationException(
                string.IsNullOrWhiteSpace(stderr) ? $"yt-dlp failed (exit {result.ExitCode})." : stderr);
        }

        var downloadedPath = RemoteAudioCache.FindDownloadedFile(token);
        if (string.IsNullOrWhiteSpace(downloadedPath) || !File.Exists(downloadedPath))
        {
            throw new InvalidOperationException("yt-dlp did not produce a playable audio file.");
        }

        if (!SupportedAudioFormats.IsSupportedExtension(downloadedPath))
        {
            throw new InvalidOperationException(
                $"yt-dlp downloaded an unsupported audio container: {Path.GetExtension(downloadedPath)}");
        }

        return downloadedPath;
    }

    private static async Task<YtDlpMediaInfo?> TryGetYtDlpInfoAsync(
        string executable,
        string url,
        CancellationToken cancellationToken)
    {
        var result = await SafeProcessRunner.RunAsync(
            executable,
            ["--dump-json", "--no-playlist", "--skip-download", "--", url],
            YtDlpInfoTimeout,
            maxOutputBytes: 2 * 1024 * 1024,
            cancellationToken);

        if (result.TimedOut || result.ExitCode != 0 || string.IsNullOrWhiteSpace(result.Stdout))
        {
            return null;
        }

        var json = result.Stdout
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault(static line => line.StartsWith('{'));
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            var durationSeconds = TryGetDouble(root, "duration");
            return new YtDlpMediaInfo(
                FirstNonEmpty(TryGetString(root, "title"), TryGetString(root, "fulltitle")),
                FirstNonEmpty(
                    TryGetString(root, "artist"),
                    TryGetString(root, "uploader"),
                    TryGetString(root, "channel")),
                TryGetString(root, "album"),
                TryGetString(root, "thumbnail"),
                TryGetString(root, "ext"),
                durationSeconds > 0 ? TimeSpan.FromSeconds(durationSeconds) : TimeSpan.Zero);
        }
        catch
        {
            return null;
        }
    }

    private static async Task<RemoteAudioResolveResult> ResolveUntitledTrackAsync(
        Uri uri,
        CancellationToken cancellationToken)
    {
        if (!IsUntitledTrackUri(uri))
        {
            throw new InvalidOperationException(
                "Untitled links must look like https://untitled.stream/library/track/{slug}.");
        }

        LogUntitled($"Resolve: {uri.AbsoluteUri}");
        var html = await Http.GetStringAsync(uri, cancellationToken);
        var remixJson = ExtractUntitledRemixContextJson(html) ??
            throw new InvalidOperationException("Untitled did not expose playable track data.");

        using var document = JsonDocument.Parse(remixJson);
        if (!TryFindUntitledTrackElement(document.RootElement, out var trackElement))
        {
            throw new InvalidOperationException("Untitled loader data did not include a playable track object.");
        }

        var fallbackUrl = TryGetUntitledString(trackElement, "audio_fallback_url", "audioFallbackUrl") ??
            throw new InvalidOperationException("Untitled track data did not include an MP3 fallback path.");
        var ownerAuthId = TryGetUntitledString(trackElement, "owner_auth_id", "ownerAuthId");
        var objectPath = ParseUntitledTranscodedObjectPath(fallbackUrl, ownerAuthId);
        var signedAudioUrl = await FetchUntitledSignedAudioUrlAsync(
            objectPath.OwnerAuthId,
            objectPath.FileName,
            cancellationToken);
        var sourceUrl = new UriBuilder(uri) { Fragment = "" }.Uri.AbsoluteUri;

        return new RemoteAudioResolveResult(
            RemoteAudioServiceKind.Untitled,
            "Untitled",
            sourceUrl,
            signedAudioUrl,
            ".mp3",
            "Untitled MP3",
            FirstNonEmpty(
                TryGetUntitledString(trackElement, "title", "name", "display_title", "displayTitle"),
                TryGetUntitledString(trackElement, "filename", "file_name"),
                Path.GetFileNameWithoutExtension(objectPath.FileName)),
            ExtractUntitledArtist(trackElement),
            "Untitled",
            NormalizeUntitledAssetUrl(
                FirstNonEmpty(
                    TryGetUntitledString(trackElement, "artwork_url", "artworkUrl"),
                    TryGetUntitledString(trackElement, "cover_art_url", "coverArtUrl"),
                    TryGetUntitledString(trackElement, "image_url", "imageUrl"),
                    TryGetUntitledString(trackElement, "thumbnail_url", "thumbnailUrl")),
                uri),
            TimeSpan.Zero,
            sourceUrl);
    }

    private static async Task<RemoteAudioResolveResult> ResolveSunoTrackAsync(
        Uri uri,
        CancellationToken cancellationToken)
    {
        var clip = await SunoClipResolver.ResolveAsync(uri.AbsoluteUri, cancellationToken);
        return new RemoteAudioResolveResult(
            RemoteAudioServiceKind.Suno,
            "Suno",
            uri.AbsoluteUri,
            clip.AudioUrl,
            ".mp3",
            "Suno MP3",
            clip.Title,
            clip.Artist,
            FirstNonEmpty(clip.Tags, "Suno"),
            FirstNonEmpty(clip.ImageLargeUrl, clip.ImageUrl),
            clip.DurationSeconds is > 0 ? TimeSpan.FromSeconds(clip.DurationSeconds.Value) : TimeSpan.Zero,
            uri.AbsoluteUri,
            LyricsText: clip.LyricsText);
    }

    private static bool IsUntitledTrackUri(Uri uri) =>
        uri.Scheme is "http" or "https" &&
        (uri.Host.Equals("untitled.stream", StringComparison.OrdinalIgnoreCase) ||
            uri.Host.EndsWith(".untitled.stream", StringComparison.OrdinalIgnoreCase)) &&
        TryGetUntitledSlug(uri) is not null;

    private static string? TryGetUntitledSlug(Uri uri)
    {
        var parts = uri.AbsolutePath
            .Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Select(Uri.UnescapeDataString)
            .ToArray();

        for (var index = 0; index + 2 < parts.Length; index++)
        {
            if (parts[index].Equals("library", StringComparison.OrdinalIgnoreCase) &&
                parts[index + 1].Equals("track", StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(parts[index + 2]))
            {
                return parts[index + 2];
            }
        }

        return null;
    }

    private static async Task<string> FetchUntitledSignedAudioUrlAsync(
        string ownerAuthId,
        string fileName,
        CancellationToken cancellationToken)
    {
        var objectPath = Uri.EscapeDataString($"{ownerAuthId}/{fileName}");
        var requestUrl =
            $"https://untitled.stream/api/storage/buckets/private-transcoded-audio/objects/{objectPath}/signedUrl?durationInSeconds=10800&cacheBufferInSeconds=600";

        using var response = await Http.GetAsync(requestUrl, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"Untitled refused to sign this track (HTTP {(int)response.StatusCode}). It may not be public.");
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var signedUrl = TryGetUntitledString(document.RootElement, "url");
        if (string.IsNullOrWhiteSpace(signedUrl))
        {
            throw new InvalidOperationException("Untitled signed URL response did not include a stream URL.");
        }

        return Uri.TryCreate(signedUrl, UriKind.Absolute, out var absolute)
            ? absolute.AbsoluteUri
            : new Uri(new Uri("https://sb.untitled.stream"), signedUrl).AbsoluteUri;
    }

    private static UntitledObjectPath ParseUntitledTranscodedObjectPath(string fallbackUrl, string? ownerAuthId)
    {
        if (!Uri.TryCreate(fallbackUrl, UriKind.Absolute, out var uri))
        {
            throw new InvalidOperationException("Untitled MP3 fallback path was not a valid URL.");
        }

        var parts = uri.AbsolutePath
            .Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Select(Uri.UnescapeDataString)
            .ToArray();
        var bucketIndex = Array.FindIndex(
            parts,
            static part => part.Equals("private-transcoded-audio", StringComparison.OrdinalIgnoreCase));

        if (bucketIndex < 0 || bucketIndex + 2 >= parts.Length)
        {
            throw new InvalidOperationException("Untitled MP3 fallback path did not include the expected object path.");
        }

        var pathOwner = parts[bucketIndex + 1];
        var fileName = parts[^1];
        var owner = FirstNonEmpty(ownerAuthId, pathOwner);
        if (string.IsNullOrWhiteSpace(owner) || string.IsNullOrWhiteSpace(fileName))
        {
            throw new InvalidOperationException("Untitled track data did not include the uploader id and MP3 filename.");
        }

        return new UntitledObjectPath(owner, fileName);
    }

    private static string? ExtractUntitledRemixContextJson(string html)
    {
        const string marker = "window.__remixContext";
        var markerIndex = html.IndexOf(marker, StringComparison.Ordinal);
        if (markerIndex < 0)
        {
            return null;
        }

        var equalsIndex = html.IndexOf('=', markerIndex + marker.Length);
        if (equalsIndex < 0)
        {
            return null;
        }

        var startIndex = html.IndexOf('{', equalsIndex + 1);
        return startIndex < 0 ? null : ExtractBalancedJsonObject(html, startIndex);
    }

    private static string? ExtractBalancedJsonObject(string value, int startIndex)
    {
        var depth = 0;
        var inString = false;
        var escaped = false;

        for (var index = startIndex; index < value.Length; index++)
        {
            var ch = value[index];
            if (inString)
            {
                if (escaped)
                {
                    escaped = false;
                }
                else if (ch == '\\')
                {
                    escaped = true;
                }
                else if (ch == '"')
                {
                    inString = false;
                }

                continue;
            }

            if (ch == '"')
            {
                inString = true;
                continue;
            }

            if (ch == '{')
            {
                depth++;
                continue;
            }

            if (ch != '}')
            {
                continue;
            }

            depth--;
            if (depth == 0)
            {
                return value[startIndex..(index + 1)];
            }
        }

        return null;
    }

    private static bool TryFindUntitledTrackElement(JsonElement element, out JsonElement trackElement)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            if (TryGetUntitledString(element, "owner_auth_id", "ownerAuthId") is not null &&
                TryGetUntitledString(element, "audio_fallback_url", "audioFallbackUrl") is not null)
            {
                trackElement = element;
                return true;
            }

            foreach (var property in element.EnumerateObject())
            {
                if (TryFindUntitledTrackElement(property.Value, out trackElement))
                {
                    return true;
                }
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                if (TryFindUntitledTrackElement(item, out trackElement))
                {
                    return true;
                }
            }
        }

        trackElement = default;
        return false;
    }

    private static string? TryGetUntitledString(JsonElement element, params string[] propertyNames)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        foreach (var propertyName in propertyNames)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (!property.Name.Equals(propertyName, StringComparison.OrdinalIgnoreCase) ||
                    property.Value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined or JsonValueKind.Object or JsonValueKind.Array)
                {
                    continue;
                }

                var value = property.Value.ValueKind == JsonValueKind.String
                    ? property.Value.GetString()
                    : property.Value.ToString();
                if (!string.IsNullOrWhiteSpace(value))
                {
                    return WebUtility.HtmlDecode(value).Trim();
                }
            }
        }

        return null;
    }

    private static string? ExtractUntitledArtist(JsonElement trackElement)
    {
        var direct = TryGetUntitledString(
            trackElement,
            "artist_name",
            "artistName",
            "artist",
            "creator_name",
            "creatorName",
            "owner_name",
            "ownerName");
        if (!string.IsNullOrWhiteSpace(direct))
        {
            return direct;
        }

        foreach (var objectName in new[] { "artist", "creator", "owner", "user", "profile" })
        {
            if (!TryGetUntitledProperty(trackElement, objectName, out var nested) ||
                nested.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var nestedName = TryGetUntitledString(nested, "display_name", "displayName", "name", "username");
            if (!string.IsNullOrWhiteSpace(nestedName))
            {
                return nestedName;
            }
        }

        return null;
    }

    private static bool TryGetUntitledProperty(JsonElement element, string propertyName, out JsonElement value)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (property.Name.Equals(propertyName, StringComparison.OrdinalIgnoreCase))
                {
                    value = property.Value;
                    return true;
                }
            }
        }

        value = default;
        return false;
    }

    private static string? NormalizeUntitledAssetUrl(string? value, Uri pageUri)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return Uri.TryCreate(value, UriKind.Absolute, out var absolute)
            ? absolute.AbsoluteUri
            : new Uri(pageUri, value).AbsoluteUri;
    }

    private static string? TryGetString(JsonElement element, string propertyName)
    {
        if (element.ValueKind != JsonValueKind.Object ||
            !element.TryGetProperty(propertyName, out var property) ||
            property.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return null;
        }

        return property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : property.ToString();
    }

    private static double TryGetDouble(JsonElement element, string propertyName)
    {
        if (element.ValueKind != JsonValueKind.Object ||
            !element.TryGetProperty(propertyName, out var property))
        {
            return 0;
        }

        return property.ValueKind == JsonValueKind.Number && property.TryGetDouble(out var value)
            ? value
            : 0;
    }

    private static string ServiceLabel(RemoteAudioServiceKind kind) =>
        kind switch
        {
            RemoteAudioServiceKind.SoundCloud => "SoundCloud",
            RemoteAudioServiceKind.YouTube => "YouTube",
            RemoteAudioServiceKind.BandLab => "BandLab",
            RemoteAudioServiceKind.Suno => "Suno",
            RemoteAudioServiceKind.Untitled => "Untitled",
            RemoteAudioServiceKind.DirectAudio => "Remote audio",
            _ => "Remote service",
        };

    private static string? ExtractYouTubeVideoId(Uri uri)
    {
        if (uri.Host.Contains("youtu.be", StringComparison.OrdinalIgnoreCase))
        {
            var id = uri.AbsolutePath.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
            return string.IsNullOrWhiteSpace(id) ? null : id;
        }

        var query = uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries);
        foreach (var part in query)
        {
            var separatorIndex = part.IndexOf('=');
            var name = separatorIndex >= 0 ? part[..separatorIndex] : part;
            if (!Uri.UnescapeDataString(name).Equals("v", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var value = separatorIndex >= 0 ? part[(separatorIndex + 1)..] : "";
            return Uri.UnescapeDataString(value.Replace("+", " "));
        }

        var match = Regex.Match(uri.AbsolutePath, @"/(?:shorts|embed)/(?<id>[^/?#]+)", RegexOptions.IgnoreCase);
        return match.Success ? Uri.UnescapeDataString(match.Groups["id"].Value) : null;
    }

    private static string NormalizeAudioExtension(string? extension, string fallback)
    {
        if (string.IsNullOrWhiteSpace(extension))
        {
            return fallback;
        }

        var normalized = extension.StartsWith('.') ? extension : "." + extension;
        return SupportedAudioFormats.Extensions.Contains(normalized, StringComparer.OrdinalIgnoreCase)
            ? normalized.ToLowerInvariant()
            : fallback;
    }

    private static string? FirstNonEmpty(params string?[] values)
    {
        foreach (var value in values)
        {
            var clean = WebUtility.HtmlDecode(value ?? string.Empty).Trim();
            if (!string.IsNullOrWhiteSpace(clean))
            {
                return clean;
            }
        }

        return null;
    }

    private static void LogUntitled(string message)
    {
        try
        {
            AppLogPaths.AppendTimestamped(UntitledLogPath, message);
        }
        catch
        {
        }
    }

    private static HttpClient CreateHttpClient()
    {
        var handler = new HttpClientHandler
        {
            AllowAutoRedirect = true,
            MaxAutomaticRedirections = 8,
        };
        var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(30) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd(
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36");
        http.DefaultRequestHeaders.Accept.ParseAdd("application/json,text/html,application/xhtml+xml,*/*");
        return http;
    }

    private sealed record YtDlpMediaInfo(
        string? Title,
        string? Artist,
        string? Album,
        string? ArtworkUrl,
        string? Extension,
        TimeSpan Duration);

    private sealed record UntitledObjectPath(string OwnerAuthId, string FileName);
}
