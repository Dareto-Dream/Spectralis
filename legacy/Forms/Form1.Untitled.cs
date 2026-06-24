using System.Net;
using System.Net.Http;
using System.Text.Json;

namespace Spectralis;

public partial class Form1
{
    private static readonly HttpClient UntitledHttp = CreateUntitledHttp();

    internal static readonly string UntitledLogPath =
        AppLogPaths.For("untitled.log");

    private static HttpClient CreateUntitledHttp()
    {
        var http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd(
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36");
        http.DefaultRequestHeaders.Accept.ParseAdd("application/json,text/html,application/xhtml+xml,*/*");
        return http;
    }

    private void LogUntitled(string message)
    {
        try { AppLogPaths.AppendTimestamped(UntitledLogPath, message); }
        catch { }
    }

    private async Task LoadUntitledUrlAsync(string url)
    {
        if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri) || !IsUntitledTrackUri(uri))
        {
            ShowError("This Untitled link is not a supported public track URL.", "Open URL");
            return;
        }

        LogUntitled($"--- LoadUntitledUrlAsync: {uri.AbsoluteUri}");

        StopRemoteAudioPlayback();
        var loadId = ++remoteAudioLoadId;
        var cts = remoteAudioLoadCts = new CancellationTokenSource();
        string? cachedPath = null;

        try
        {
            if (IsYouTubeActive)
                StopYouTubePlayback();
            if (IsSoundCloudActive)
                StopSoundCloudPlayback();
            if (IsSunoActive)
                StopSunoPlayback();
            if (IsSpotifyActive || spotifyService.IsLinked)
                ParkSpotifyForLocalPlayback(resumeAfterLocalPlayback: false, advanceOnResume: false);

            StopLocalPlaybackForExternalUrl();
            if (engine.IsLoaded)
                engine.Unload();
            UpdateUiState();

            var resolved = await ResolveUntitledTrackAsync(uri.AbsoluteUri, cts.Token);
            if (cts.IsCancellationRequested || loadId != remoteAudioLoadId)
                return;

            cachedPath = await RemoteAudioCache.DownloadAsync(
                resolved.SignedAudioUrl,
                ".mp3",
                cts.Token,
                requestInitialRange: true);
            if (cts.IsCancellationRequested || loadId != remoteAudioLoadId)
            {
                RemoteAudioCache.TryDelete(cachedPath);
                return;
            }

            var metadata = AudioMetadataReader.Read(cachedPath);
            var artBytes = metadata.AlbumArtBytes ??
                await TryFetchUntitledArtworkBytesAsync(resolved.ArtworkUrl, cts.Token);

            var title = FirstNonEmptyUntitled(
                resolved.Title,
                metadata.Title,
                Path.GetFileNameWithoutExtension(resolved.FileName),
                "Untitled track")!;
            var trackInfo = new AudioTrackInfo(
                resolved.SourceId,
                title,
                FirstNonEmptyUntitled(metadata.Artist, resolved.Artist),
                FirstNonEmptyUntitled(metadata.Album, "Untitled"),
                artBytes,
                metadata.Lyrics,
                metadata.EmbeddedVisualizer,
                metadata.EmbeddedTheme,
                metadata.EmbeddedHtml,
                metadata.EmbeddedMarkdown,
                metadata.EmbeddedVideo,
                "Untitled MP3",
                2,
                44100,
                16,
                TimeSpan.Zero);

            engine.Load(cachedPath, trackInfo);
            engine.Volume = trackBarVolume.Value / 100f;
            remoteAudioTempPath = cachedPath;
            cachedPath = null;
            engine.Play();
            UpdateAlbumArt(engine.CurrentTrack);
            UpdateUiState();
            LogUntitled($"Native playback started from signed MP3: {resolved.FileName}");
        }
        catch (OperationCanceledException)
        {
            RemoteAudioCache.TryDelete(cachedPath);
        }
        catch (Exception ex)
        {
            RemoteAudioCache.TryDelete(cachedPath);
            LogUntitled($"Load failed: {ex}");
            ShowError(
                $"Spectralis could not open this Untitled track.{Environment.NewLine}{Environment.NewLine}{ex.Message}",
                "Open URL");
        }
    }

    private async Task PrepareUntitledClipboardPromptAsync(ClipboardPromptSession session)
    {
        var cancellationToken = session.Cancellation.Token;
        var resolved = await ResolveUntitledTrackAsync(session.Candidate.Url, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        session.Track = new ClipboardDetectedTrack(
            session.Candidate.Url,
            session.Candidate.Target,
            FirstNonEmptyUntitled(resolved.Title, Path.GetFileNameWithoutExtension(resolved.FileName), "Untitled track")!,
            resolved.Artist,
            "Untitled",
            resolved.ArtworkUrl);
        session.PreparedPlayback = ClipboardPreparedPlayback.StreamOnAccept();
        ShowOrUpdateClipboardPopup(session, "Ready", isPreparing: false);
    }

    private static bool IsUntitledInput(string input) =>
        Uri.TryCreate(input, UriKind.Absolute, out var uri) && IsUntitledTrackUri(uri);

    private static bool IsUntitledTrackUri(Uri uri) =>
        uri.Scheme is "http" or "https" &&
        IsUntitledHost(uri.Host) &&
        TryGetUntitledSlug(uri) is not null;

    private static bool IsUntitledHost(string host) =>
        host.Equals("untitled.stream", StringComparison.OrdinalIgnoreCase) ||
        host.EndsWith(".untitled.stream", StringComparison.OrdinalIgnoreCase);

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

    private static async Task<UntitledResolvedTrack> ResolveUntitledTrackAsync(
        string url,
        CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || !IsUntitledTrackUri(uri))
            throw new InvalidOperationException("Untitled track URLs must look like https://untitled.stream/library/track/{slug}.");

        var html = await UntitledHttp.GetStringAsync(uri, cancellationToken);
        var remixJson = ExtractUntitledRemixContextJson(html) ??
            throw new InvalidOperationException("Untitled did not expose the expected window.__remixContext data.");

        using var document = JsonDocument.Parse(remixJson);
        if (!TryFindUntitledTrackElement(document.RootElement, out var trackElement))
            throw new InvalidOperationException("Untitled loader data did not include a playable track object.");

        var fallbackUrl = TryGetUntitledString(trackElement, "audio_fallback_url", "audioFallbackUrl") ??
            throw new InvalidOperationException("Untitled track data did not include an MP3 fallback path.");
        var ownerAuthId = TryGetUntitledString(trackElement, "owner_auth_id", "ownerAuthId");

        var objectPath = ParseUntitledTranscodedObjectPath(fallbackUrl, ownerAuthId);
        var signedAudioUrl = await FetchUntitledSignedAudioUrlAsync(objectPath.OwnerAuthId, objectPath.FileName, cancellationToken);
        var sourceUrl = new UriBuilder(uri) { Fragment = "" }.Uri.AbsoluteUri;

        return new UntitledResolvedTrack(
            $"untitled:{TryGetUntitledSlug(uri) ?? objectPath.FileName}",
            sourceUrl,
            objectPath.OwnerAuthId,
            objectPath.FileName,
            signedAudioUrl,
            FirstNonEmptyUntitled(
                TryGetUntitledString(trackElement, "title", "name", "display_title", "displayTitle"),
                TryGetUntitledString(trackElement, "filename", "file_name")),
            ExtractUntitledArtist(trackElement),
            NormalizeUntitledAssetUrl(
                FirstNonEmptyUntitled(
                    TryGetUntitledString(trackElement, "artwork_url", "artworkUrl"),
                    TryGetUntitledString(trackElement, "cover_art_url", "coverArtUrl"),
                    TryGetUntitledString(trackElement, "image_url", "imageUrl"),
                    TryGetUntitledString(trackElement, "thumbnail_url", "thumbnailUrl")),
                uri));
    }

    private static async Task<string> FetchUntitledSignedAudioUrlAsync(
        string ownerAuthId,
        string fileName,
        CancellationToken cancellationToken)
    {
        var objectPath = Uri.EscapeDataString($"{ownerAuthId}/{fileName}");
        var requestUrl =
            $"https://untitled.stream/api/storage/buckets/private-transcoded-audio/objects/{objectPath}/signedUrl?durationInSeconds=10800&cacheBufferInSeconds=600";

        using var response = await UntitledHttp.GetAsync(requestUrl, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"Untitled refused to sign this track (HTTP {(int)response.StatusCode}). It may not be public.");
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var signedUrl = TryGetUntitledString(document.RootElement, "url");
        if (string.IsNullOrWhiteSpace(signedUrl))
            throw new InvalidOperationException("Untitled signed URL response did not include a stream URL.");

        return Uri.TryCreate(signedUrl, UriKind.Absolute, out var absolute)
            ? absolute.AbsoluteUri
            : new Uri(new Uri("https://sb.untitled.stream"), signedUrl).AbsoluteUri;
    }

    private static UntitledObjectPath ParseUntitledTranscodedObjectPath(string fallbackUrl, string? ownerAuthId)
    {
        if (!Uri.TryCreate(fallbackUrl, UriKind.Absolute, out var uri))
            throw new InvalidOperationException("Untitled MP3 fallback path was not a valid URL.");

        var parts = uri.AbsolutePath
            .Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Select(Uri.UnescapeDataString)
            .ToArray();
        var bucketIndex = Array.FindIndex(
            parts,
            static part => part.Equals("private-transcoded-audio", StringComparison.OrdinalIgnoreCase));

        if (bucketIndex < 0 || bucketIndex + 2 >= parts.Length)
            throw new InvalidOperationException("Untitled MP3 fallback path did not include the expected storage object path.");

        var pathOwner = parts[bucketIndex + 1];
        var fileName = parts[^1];
        var owner = FirstNonEmptyUntitled(ownerAuthId, pathOwner);
        if (string.IsNullOrWhiteSpace(owner) || string.IsNullOrWhiteSpace(fileName))
            throw new InvalidOperationException("Untitled track data did not include the uploader id and MP3 filename.");

        return new UntitledObjectPath(owner, fileName);
    }

    private static string? ExtractUntitledRemixContextJson(string html)
    {
        const string marker = "window.__remixContext";
        var markerIndex = html.IndexOf(marker, StringComparison.Ordinal);
        if (markerIndex < 0)
            return null;

        var equalsIndex = html.IndexOf('=', markerIndex + marker.Length);
        if (equalsIndex < 0)
            return null;

        var startIndex = html.IndexOf('{', equalsIndex + 1);
        if (startIndex < 0)
            return null;

        return ExtractBalancedJsonObject(html, startIndex);
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
                continue;

            depth--;
            if (depth == 0)
                return value[startIndex..(index + 1)];
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
                    return true;
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                if (TryFindUntitledTrackElement(item, out trackElement))
                    return true;
            }
        }

        trackElement = default;
        return false;
    }

    private static string? TryGetUntitledString(JsonElement element, params string[] propertyNames)
    {
        if (element.ValueKind != JsonValueKind.Object)
            return null;

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
                    return WebUtility.HtmlDecode(value).Trim();
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
            return direct;

        foreach (var objectName in new[] { "artist", "creator", "owner", "user", "profile" })
        {
            if (!TryGetUntitledProperty(trackElement, objectName, out var nested) ||
                nested.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var nestedName = TryGetUntitledString(nested, "display_name", "displayName", "name", "username");
            if (!string.IsNullOrWhiteSpace(nestedName))
                return nestedName;
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
            return null;

        return Uri.TryCreate(value, UriKind.Absolute, out var absolute)
            ? absolute.AbsoluteUri
            : new Uri(pageUri, value).AbsoluteUri;
    }

    private static async Task<byte[]?> TryFetchUntitledArtworkBytesAsync(
        string? artworkUrl,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(artworkUrl))
            return null;

        try
        {
            return await UntitledHttp.GetByteArrayAsync(artworkUrl, cancellationToken);
        }
        catch
        {
            return null;
        }
    }

    private static string? FirstNonEmptyUntitled(params string?[] values)
    {
        foreach (var value in values)
        {
            var clean = WebUtility.HtmlDecode(value ?? "").Trim();
            if (!string.IsNullOrWhiteSpace(clean))
                return clean;
        }

        return null;
    }

    private sealed record UntitledResolvedTrack(
        string SourceId,
        string SourceUrl,
        string OwnerAuthId,
        string FileName,
        string SignedAudioUrl,
        string? Title,
        string? Artist,
        string? ArtworkUrl);

    private sealed record UntitledObjectPath(string OwnerAuthId, string FileName);
}
