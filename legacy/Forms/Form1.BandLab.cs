using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using TagLib;

namespace Spectralis;

public partial class Form1
{
    private static readonly HttpClient BandLabHttp = CreateBandLabHttp();

    internal static readonly string BandLabLogPath =
        AppLogPaths.For("bandlab.log");

    private static HttpClient CreateBandLabHttp()
    {
        var http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd(
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36");
        http.DefaultRequestHeaders.Accept.ParseAdd("application/json,text/html,application/xhtml+xml,*/*");
        http.DefaultRequestHeaders.Referrer = new Uri("https://www.bandlab.com/");
        return http;
    }

    private void LogBandLab(string message)
    {
        try { AppLogPaths.AppendTimestamped(BandLabLogPath, message); }
        catch { }
    }

    private async Task LoadBandLabUrlAsync(string url)
    {
        if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri) || !IsBandLabTrackUri(uri))
        {
            ShowError("This BandLab link is not a supported track URL.", "Open URL");
            return;
        }

        LogBandLab($"--- LoadBandLabUrlAsync: {uri.AbsoluteUri}");

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

            var resolved = await ResolveBandLabTrackAsync(uri.AbsoluteUri, cts.Token);
            if (cts.IsCancellationRequested || loadId != remoteAudioLoadId)
                return;

            var extension = GetBandLabAudioExtension(resolved.AudioUrl);
            cachedPath = await RemoteAudioCache.DownloadAsync(
                resolved.AudioUrl,
                extension,
                cts.Token,
                requestInitialRange: true,
                referer: "https://www.bandlab.com/");
            if (cts.IsCancellationRequested || loadId != remoteAudioLoadId)
            {
                RemoteAudioCache.TryDelete(cachedPath);
                return;
            }

            var artBytes = await TryFetchBandLabArtworkBytesAsync(resolved.ArtworkUrl, cts.Token);
            ApplyBandLabMetadataTags(cachedPath, resolved, artBytes);

            var metadata = AudioMetadataReader.Read(cachedPath);
            artBytes ??= metadata.AlbumArtBytes;

            var title = FirstNonEmptyBandLab(
                resolved.Title,
                metadata.Title,
                Path.GetFileNameWithoutExtension(new Uri(resolved.AudioUrl).AbsolutePath),
                "BandLab track")!;
            var trackInfo = new AudioTrackInfo(
                resolved.SourceId,
                title,
                FirstNonEmptyBandLab(metadata.Artist, resolved.Artist),
                FirstNonEmptyBandLab(metadata.Album, resolved.Album, "BandLab"),
                artBytes,
                metadata.Lyrics ?? BuildBandLabLyrics(resolved.LyricsText),
                metadata.EmbeddedVisualizer,
                metadata.EmbeddedTheme,
                metadata.EmbeddedHtml,
                metadata.EmbeddedMarkdown,
                metadata.EmbeddedVideo,
                "BandLab M4A",
                2,
                44100,
                16,
                resolved.DurationSeconds is > 0
                    ? TimeSpan.FromSeconds(resolved.DurationSeconds.Value)
                    : TimeSpan.Zero);

            engine.Load(cachedPath, trackInfo);
            engine.Volume = trackBarVolume.Value / 100f;
            remoteAudioTempPath = cachedPath;
            cachedPath = null;
            engine.Play();
            UpdateAlbumArt(engine.CurrentTrack);
            UpdateUiState();
            LogBandLab($"Native playback started from CDN file: {resolved.AudioUrl}");
        }
        catch (OperationCanceledException)
        {
            RemoteAudioCache.TryDelete(cachedPath);
        }
        catch (Exception ex)
        {
            RemoteAudioCache.TryDelete(cachedPath);
            LogBandLab($"Load failed: {ex}");
            ShowError(
                $"Spectralis could not open this BandLab track.{Environment.NewLine}{Environment.NewLine}{ex.Message}",
                "Open URL");
        }
    }

    private async Task PrepareBandLabClipboardPromptAsync(ClipboardPromptSession session)
    {
        var cancellationToken = session.Cancellation.Token;
        var resolved = await ResolveBandLabTrackAsync(session.Candidate.Url, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        session.Track = new ClipboardDetectedTrack(
            session.Candidate.Url,
            session.Candidate.Target,
            FirstNonEmptyBandLab(resolved.Title, "BandLab track")!,
            resolved.Artist,
            FirstNonEmptyBandLab(resolved.Genre, "BandLab")!,
            resolved.ArtworkUrl);
        session.PreparedPlayback = ClipboardPreparedPlayback.StreamOnAccept();
        ShowOrUpdateClipboardPopup(session, "Ready", isPreparing: false);
    }

    private static bool IsBandLabInput(string input) =>
        Uri.TryCreate(input, UriKind.Absolute, out var uri) && IsBandLabTrackUri(uri);

    private static bool IsBandLabTrackUri(Uri uri)
    {
        if (uri.Scheme is not ("http" or "https") || !IsBandLabHost(uri.Host))
            return false;

        var parts = GetBandLabPathParts(uri);
        return parts.Length switch
        {
            >= 2 when parts[0].Equals("track", StringComparison.OrdinalIgnoreCase) => true,
            >= 2 when parts[0].Equals("revisions", StringComparison.OrdinalIgnoreCase) => true,
            >= 2 when IsBandLabReservedPath(parts[0]) => false,
            >= 2 => true,
            _ => false
        };
    }

    private static bool IsBandLabHost(string host) =>
        host.Equals("bandlab.com", StringComparison.OrdinalIgnoreCase) ||
        host.Equals("www.bandlab.com", StringComparison.OrdinalIgnoreCase);

    private static string[] GetBandLabPathParts(Uri uri) =>
        uri.AbsolutePath
            .Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Select(Uri.UnescapeDataString)
            .ToArray();

    private static bool IsBandLabReservedPath(string path) =>
        path.Equals("api", StringComparison.OrdinalIgnoreCase) ||
        path.Equals("feed", StringComparison.OrdinalIgnoreCase) ||
        path.Equals("discover", StringComparison.OrdinalIgnoreCase) ||
        path.Equals("library", StringComparison.OrdinalIgnoreCase) ||
        path.Equals("sounds", StringComparison.OrdinalIgnoreCase) ||
        path.Equals("albums", StringComparison.OrdinalIgnoreCase);

    private static async Task<BandLabResolvedTrack> ResolveBandLabTrackAsync(
        string url,
        CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || !IsBandLabTrackUri(uri))
            throw new InvalidOperationException("BandLab track URLs must look like https://www.bandlab.com/track/{postId}, https://www.bandlab.com/revisions/{sharedPostId}, or https://www.bandlab.com/{username}/{songSlug}.");

        var sharedKey = GetBandLabSharedKey(uri);
        if (TryGetBandLabPostId(uri, out var postId))
        {
            return await ResolveBandLabPostApiAsync(uri, postId, sharedKey, cancellationToken);
        }

        if (TryGetBandLabRevisionSharePostId(uri, out var revisionSharePostId))
        {
            return await ResolveBandLabPostApiAsync(uri, revisionSharePostId, sharedKey, cancellationToken);
        }

        return await ResolveBandLabPageStateAsync(uri, cancellationToken);
    }

    private static async Task<BandLabResolvedTrack> ResolveBandLabPostApiAsync(
        Uri sourceUri,
        string postId,
        string? sharedKey,
        CancellationToken cancellationToken)
    {
        var requestUrl = $"https://www.bandlab.com/api/v1.3/posts/{Uri.EscapeDataString(postId)}";
        if (!string.IsNullOrWhiteSpace(sharedKey))
            requestUrl += $"?sharedKey={Uri.EscapeDataString(sharedKey)}";

        using var response = await BandLabHttp.GetAsync(requestUrl, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"BandLab refused this post metadata request (HTTP {(int)response.StatusCode}). It may need a shared key.");
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        if (!TryFindBandLabPlayablePost(document.RootElement, out var postElement, out var audioUrl))
            throw new InvalidOperationException("BandLab post metadata did not include revision.mixdown.file.");

        return BuildBandLabResolvedTrack(sourceUri, postId, postElement, audioUrl);
    }

    private static async Task<BandLabResolvedTrack> ResolveBandLabPageStateAsync(
        Uri sourceUri,
        CancellationToken cancellationToken)
    {
        var html = await BandLabHttp.GetStringAsync(sourceUri, cancellationToken);

        var directAudioUrl = TryExtractBandLabStaticAudioUrl(html);
        foreach (var json in EnumerateBandLabJsonCandidates(html))
        {
            try
            {
                using var document = JsonDocument.Parse(json);
                if (!TryFindBandLabPlayablePost(document.RootElement, out var postElement, out var audioUrl))
                    continue;

                return BuildBandLabResolvedTrack(
                    sourceUri,
                    TryGetBandLabString(postElement, "id", "postId", "post_id") ?? TryBuildBandLabSourceKey(sourceUri),
                    postElement,
                    audioUrl);
            }
            catch (JsonException)
            {
            }
        }

        if (!string.IsNullOrWhiteSpace(directAudioUrl))
        {
            return new BandLabResolvedTrack(
                $"bandlab:{TryBuildBandLabSourceKey(sourceUri)}",
                sourceUri.AbsoluteUri,
                directAudioUrl,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null);
        }

        throw new InvalidOperationException("BandLab page data did not include a playable mixdown URL.");
    }

    private static BandLabResolvedTrack BuildBandLabResolvedTrack(
        Uri sourceUri,
        string postId,
        JsonElement postElement,
        string audioUrl)
    {
        var revision = TryGetBandLabProperty(postElement, "revision", out var revisionEl) &&
            revisionEl.ValueKind == JsonValueKind.Object
            ? revisionEl
            : default;
        var song = revision.ValueKind == JsonValueKind.Object &&
            TryGetBandLabProperty(revision, "song", out var songEl) &&
            songEl.ValueKind == JsonValueKind.Object
            ? songEl
            : default;
        var creator = TryGetBandLabProperty(postElement, "creator", out var creatorEl) &&
            creatorEl.ValueKind == JsonValueKind.Object
            ? creatorEl
            : default;

        return new BandLabResolvedTrack(
            $"bandlab:{postId}",
            sourceUri.AbsoluteUri,
            NormalizeBandLabUrl(audioUrl, sourceUri),
            FirstNonEmptyBandLab(
                TryGetBandLabString(song, "name", "title"),
                TryGetBandLabNestedString(postElement, "revision", "name"),
                TryGetBandLabString(postElement, "title", "name", "caption"),
                TryGetBandLabNestedString(postElement, "song", "name")),
            FirstNonEmptyBandLab(
                TryGetBandLabString(creator, "name"),
                TryGetBandLabString(creator, "username"),
                TryGetBandLabNestedString(postElement, "creator", "name"),
                TryGetBandLabNestedString(postElement, "creator", "username"),
                TryGetBandLabNestedString(postElement, "user", "name"),
                TryGetBandLabNestedString(postElement, "owner", "name")),
            FirstNonEmptyBandLab(TryGetBandLabString(song, "name"), "BandLab"),
            TryGetBandLabString(song, "slug"),
            TryGetBandLabString(creator, "username"),
            TryGetBandLabString(creator, "id"),
            TryGetBandLabGenre(revision),
            TryGetBandLabNestedDouble(revision, "mixdown", "duration"),
            TryGetBandLabNestedString(revision, "lyrics", "content"),
            NormalizeOptionalBandLabUrl(GetBandLabSizedPictureUrl(song, "640x640"), sourceUri),
            TryGetBandLabNestedString(song, "picture", "color"),
            TryGetBandLabNestedString(song, "picture", "adjustedColor"),
            TryGetBandLabNestedInt64(revision, "counters", "plays"),
            TryGetBandLabNestedInt64(revision, "counters", "likes"),
            TryGetBandLabNestedInt64(revision, "counters", "forks"));
    }

    private static bool TryGetBandLabPostId(Uri uri, out string postId)
    {
        var parts = GetBandLabPathParts(uri);
        if (parts.Length >= 2 && parts[0].Equals("track", StringComparison.OrdinalIgnoreCase))
        {
            postId = parts[1];
            return !string.IsNullOrWhiteSpace(postId);
        }

        postId = "";
        return false;
    }

    private static bool TryGetBandLabRevisionSharePostId(Uri uri, out string postId)
    {
        var parts = GetBandLabPathParts(uri);
        if (parts.Length >= 2 && parts[0].Equals("revisions", StringComparison.OrdinalIgnoreCase))
        {
            postId = parts[1];
            return !string.IsNullOrWhiteSpace(postId);
        }

        postId = "";
        return false;
    }

    private static string? GetBandLabSharedKey(Uri uri)
    {
        foreach (var part in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var separatorIndex = part.IndexOf('=');
            var name = separatorIndex >= 0 ? part[..separatorIndex] : part;
            if (!Uri.UnescapeDataString(name).Equals("sharedKey", StringComparison.OrdinalIgnoreCase))
                continue;

            var value = separatorIndex >= 0 ? part[(separatorIndex + 1)..] : "";
            return Uri.UnescapeDataString(value.Replace("+", " "));
        }

        return null;
    }

    private static bool TryFindBandLabPlayablePost(
        JsonElement element,
        out JsonElement postElement,
        out string audioUrl)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var candidateUrl = TryGetBandLabMixdownFile(element);
            if (!string.IsNullOrWhiteSpace(candidateUrl))
            {
                postElement = element;
                audioUrl = candidateUrl;
                return true;
            }

            foreach (var property in element.EnumerateObject())
            {
                if (TryFindBandLabPlayablePost(property.Value, out postElement, out audioUrl))
                    return true;
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                if (TryFindBandLabPlayablePost(item, out postElement, out audioUrl))
                    return true;
            }
        }

        postElement = default;
        audioUrl = "";
        return false;
    }

    private static string? TryGetBandLabMixdownFile(JsonElement element)
    {
        if (!TryGetBandLabProperty(element, "revision", out var revision) ||
            revision.ValueKind != JsonValueKind.Object ||
            !TryGetBandLabProperty(revision, "mixdown", out var mixdown) ||
            mixdown.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        return TryGetBandLabString(mixdown, "file");
    }

    private static IEnumerable<string> EnumerateBandLabJsonCandidates(string html)
    {
        foreach (var scriptJson in EnumerateJsonScriptContents(html))
            yield return scriptJson;

        foreach (var marker in new[] { "__bl_bootstrap", "__BL_BOOTSTRAP__", "__NEXT_DATA__", "window.__" })
        {
            var searchIndex = 0;
            while (searchIndex < html.Length)
            {
                var markerIndex = html.IndexOf(marker, searchIndex, StringComparison.Ordinal);
                if (markerIndex < 0)
                    break;

                var startIndex = html.IndexOf('{', markerIndex + marker.Length);
                if (startIndex < 0)
                    break;

                var json = ExtractBalancedJsonObject(html, startIndex);
                if (!string.IsNullOrWhiteSpace(json))
                    yield return WebUtility.HtmlDecode(json);

                searchIndex = markerIndex + marker.Length;
            }
        }
    }

    private static IEnumerable<string> EnumerateJsonScriptContents(string html)
    {
        foreach (Match match in Regex.Matches(
            html,
            """<script\b[^>]*type\s*=\s*["']application/json["'][^>]*>(?<json>.*?)</script>""",
            RegexOptions.IgnoreCase | RegexOptions.Singleline))
        {
            var json = WebUtility.HtmlDecode(match.Groups["json"].Value).Trim();
            if (!string.IsNullOrWhiteSpace(json))
                yield return json;
        }
    }

    private static string? TryExtractBandLabStaticAudioUrl(string html)
    {
        var match = Regex.Match(
            html,
            """https:\\?/\\?/static\.bandlab\.com\\?/revisions-formatted\\?/[^"'\\<>\s]+?\.m4a""",
            RegexOptions.IgnoreCase);
        if (!match.Success)
            return null;

        return match.Value
            .Replace(@"\/", "/", StringComparison.Ordinal)
            .Replace(@"\u002F", "/", StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryGetBandLabProperty(JsonElement element, string propertyName, out JsonElement value)
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

    private static string? TryGetBandLabString(JsonElement element, params string[] propertyNames)
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

    private static string? TryGetBandLabNestedString(JsonElement element, string objectName, string propertyName)
    {
        return TryGetBandLabProperty(element, objectName, out var nested) && nested.ValueKind == JsonValueKind.Object
            ? TryGetBandLabString(nested, propertyName)
            : null;
    }

    private static double? TryGetBandLabDouble(JsonElement element, string propertyName)
    {
        if (!TryGetBandLabProperty(element, propertyName, out var value) ||
            value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return null;
        }

        return value.TryGetDouble(out var result) ? result : null;
    }

    private static double? TryGetBandLabNestedDouble(JsonElement element, string objectName, string propertyName)
    {
        return TryGetBandLabProperty(element, objectName, out var nested) && nested.ValueKind == JsonValueKind.Object
            ? TryGetBandLabDouble(nested, propertyName)
            : null;
    }

    private static long? TryGetBandLabInt64(JsonElement element, string propertyName)
    {
        if (!TryGetBandLabProperty(element, propertyName, out var value) ||
            value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return null;
        }

        return value.TryGetInt64(out var result) ? result : null;
    }

    private static long? TryGetBandLabNestedInt64(JsonElement element, string objectName, string propertyName)
    {
        return TryGetBandLabProperty(element, objectName, out var nested) && nested.ValueKind == JsonValueKind.Object
            ? TryGetBandLabInt64(nested, propertyName)
            : null;
    }

    private static string? TryGetBandLabGenre(JsonElement revision)
    {
        if (!TryGetBandLabProperty(revision, "genres", out var genres) ||
            genres.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        foreach (var genre in genres.EnumerateArray())
        {
            var name = TryGetBandLabString(genre, "name");
            if (!string.IsNullOrWhiteSpace(name))
                return name;
        }

        return null;
    }

    private static string? GetBandLabSizedPictureUrl(JsonElement song, string size)
    {
        if (!TryGetBandLabProperty(song, "picture", out var picture) ||
            picture.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var baseUrl = TryGetBandLabString(picture, "url");
        if (string.IsNullOrWhiteSpace(baseUrl))
            return null;

        return baseUrl.EndsWith("/", StringComparison.Ordinal)
            ? baseUrl + size
            : baseUrl;
    }

    private static string NormalizeBandLabUrl(string value, Uri pageUri)
    {
        return Uri.TryCreate(value, UriKind.Absolute, out var absolute)
            ? absolute.AbsoluteUri
            : new Uri(pageUri, value).AbsoluteUri;
    }

    private static string? NormalizeOptionalBandLabUrl(string? value, Uri pageUri)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        return NormalizeBandLabUrl(value, pageUri);
    }

    private static string GetBandLabAudioExtension(string audioUrl)
    {
        if (Uri.TryCreate(audioUrl, UriKind.Absolute, out var uri))
        {
            var extension = Path.GetExtension(uri.AbsolutePath);
            if (!string.IsNullOrWhiteSpace(extension))
                return extension;
        }

        return ".m4a";
    }

    private static string TryBuildBandLabSourceKey(Uri uri)
    {
        var builder = new UriBuilder(uri) { Query = "", Fragment = "" };
        return builder.Uri.AbsolutePath.Trim('/').Replace('/', ':');
    }

    private static async Task<byte[]?> TryFetchBandLabArtworkBytesAsync(
        string? artworkUrl,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(artworkUrl))
            return null;

        try
        {
            return await BandLabHttp.GetByteArrayAsync(artworkUrl, cancellationToken);
        }
        catch
        {
            return null;
        }
    }

    private static void ApplyBandLabMetadataTags(
        string path,
        BandLabResolvedTrack track,
        byte[]? artworkBytes)
    {
        try
        {
            using var file = TagLib.File.Create(path);
            var tag = file.Tag;

            tag.Title = FirstNonEmptyBandLab(track.Title, tag.Title);
            var artist = FirstNonEmptyBandLab(track.Artist, tag.FirstPerformer);
            if (!string.IsNullOrWhiteSpace(artist))
            {
                tag.Performers = [artist];
                tag.AlbumArtists = [artist];
            }

            tag.Album = FirstNonEmptyBandLab(track.Album, track.Title, tag.Album);
            var genre = FirstNonEmptyBandLab(track.Genre, tag.FirstGenre);
            if (!string.IsNullOrWhiteSpace(genre))
                tag.Genres = [genre];

            if (!string.IsNullOrWhiteSpace(track.LyricsText))
                tag.Lyrics = track.LyricsText;

            if (artworkBytes is { Length: > 0 })
            {
                tag.Pictures =
                [
                    new Picture
                    {
                        Type = PictureType.FrontCover,
                        MimeType = "image/jpeg",
                        Description = "Cover",
                        Data = new ByteVector(artworkBytes)
                    }
                ];
            }

            file.Save();
        }
        catch
        {
            // Tagging is best-effort; playback can still use the API metadata.
        }
    }

    private static LyricsDocument? BuildBandLabLyrics(string? lyricsText)
    {
        if (string.IsNullOrWhiteSpace(lyricsText))
            return null;

        var parsed = LrcParser.Parse(lyricsText, "BandLab lyrics");
        if (parsed is not null)
            return parsed;

        var lines = lyricsText
            .Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n')
            .Split('\n')
            .Select(static line => line.Trim())
            .Where(static line => line.Length > 0)
            .Select(static (line, index) => new LyricsLine(index, line))
            .ToArray();

        return lines.Length > 0
            ? new LyricsDocument(lines, sourceLabel: "BandLab lyrics", isDescription: true)
            : null;
    }

    private static string? FirstNonEmptyBandLab(params string?[] values)
    {
        foreach (var value in values)
        {
            var clean = WebUtility.HtmlDecode(value ?? "").Trim();
            if (!string.IsNullOrWhiteSpace(clean))
                return clean;
        }

        return null;
    }

    private sealed record BandLabResolvedTrack(
        string SourceId,
        string SourceUrl,
        string AudioUrl,
        string? Title,
        string? Artist,
        string? Album,
        string? Slug,
        string? ArtistUsername,
        string? ArtistId,
        string? Genre,
        double? DurationSeconds,
        string? LyricsText,
        string? ArtworkUrl,
        string? CoverColor,
        string? CoverAdjustedColor,
        long? Plays,
        long? Likes,
        long? Forks);
}
