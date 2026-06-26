using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Spectralis.App.Services;

internal static class BandLabNativeResolver
{
    private static readonly HttpClient Http = CreateHttpClient();

    public static string BandLabLogPath => AppLogPaths.For("bandlab.log");

    public static async Task<RemoteAudioResolveResult> ResolveAsync(
        Uri uri,
        CancellationToken cancellationToken)
    {
        if (!IsBandLabTrackUri(uri))
        {
            throw new InvalidOperationException(
                "BandLab track URLs must look like https://www.bandlab.com/track/{postId}, https://www.bandlab.com/revisions/{sharedPostId}, or https://www.bandlab.com/{username}/{songSlug}.");
        }

        Log($"Resolve: {uri.AbsoluteUri}");
        var resolved = await ResolveBandLabTrackAsync(uri, cancellationToken);
        return new RemoteAudioResolveResult(
            RemoteAudioServiceKind.BandLab,
            "BandLab",
            resolved.SourceUrl,
            resolved.AudioUrl,
            GetAudioExtension(resolved.AudioUrl),
            "BandLab M4A",
            resolved.Title,
            resolved.Artist,
            FirstNonEmpty(resolved.Album, resolved.Genre, "BandLab"),
            resolved.ArtworkUrl,
            resolved.DurationSeconds is > 0 ? TimeSpan.FromSeconds(resolved.DurationSeconds.Value) : TimeSpan.Zero,
            "https://www.bandlab.com/",
            LyricsText: resolved.LyricsText);
    }

    private static async Task<BandLabResolvedTrack> ResolveBandLabTrackAsync(
        Uri uri,
        CancellationToken cancellationToken)
    {
        var sharedKey = GetSharedKey(uri);
        if (TryGetPostId(uri, out var postId))
        {
            return await ResolvePostApiAsync(uri, postId, sharedKey, cancellationToken);
        }

        if (TryGetRevisionSharePostId(uri, out var revisionSharePostId))
        {
            return await ResolvePostApiAsync(uri, revisionSharePostId, sharedKey, cancellationToken);
        }

        return await ResolvePageStateAsync(uri, cancellationToken);
    }

    private static async Task<BandLabResolvedTrack> ResolvePostApiAsync(
        Uri sourceUri,
        string postId,
        string? sharedKey,
        CancellationToken cancellationToken)
    {
        var requestUrl = $"https://www.bandlab.com/api/v1.3/posts/{Uri.EscapeDataString(postId)}";
        if (!string.IsNullOrWhiteSpace(sharedKey))
        {
            requestUrl += $"?sharedKey={Uri.EscapeDataString(sharedKey)}";
        }

        using var response = await Http.GetAsync(requestUrl, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"BandLab refused this post metadata request (HTTP {(int)response.StatusCode}). It may need a shared key.");
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        if (!TryFindPlayablePost(document.RootElement, out var postElement, out var audioUrl))
        {
            throw new InvalidOperationException("BandLab post metadata did not include revision.mixdown.file.");
        }

        return BuildResolvedTrack(sourceUri, postId, postElement, audioUrl);
    }

    private static async Task<BandLabResolvedTrack> ResolvePageStateAsync(
        Uri sourceUri,
        CancellationToken cancellationToken)
    {
        var html = await Http.GetStringAsync(sourceUri, cancellationToken);
        var directAudioUrl = TryExtractStaticAudioUrl(html);
        foreach (var json in EnumerateJsonCandidates(html))
        {
            try
            {
                using var document = JsonDocument.Parse(json);
                if (!TryFindPlayablePost(document.RootElement, out var postElement, out var audioUrl))
                {
                    continue;
                }

                return BuildResolvedTrack(
                    sourceUri,
                    TryGetString(postElement, "id", "postId", "post_id") ?? BuildSourceKey(sourceUri),
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
                $"bandlab:{BuildSourceKey(sourceUri)}",
                sourceUri.AbsoluteUri,
                directAudioUrl,
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

    private static BandLabResolvedTrack BuildResolvedTrack(
        Uri sourceUri,
        string postId,
        JsonElement postElement,
        string audioUrl)
    {
        var revision = TryGetProperty(postElement, "revision", out var revisionEl) &&
            revisionEl.ValueKind == JsonValueKind.Object
            ? revisionEl
            : default;
        var song = revision.ValueKind == JsonValueKind.Object &&
            TryGetProperty(revision, "song", out var songEl) &&
            songEl.ValueKind == JsonValueKind.Object
            ? songEl
            : default;
        var creator = TryGetProperty(postElement, "creator", out var creatorEl) &&
            creatorEl.ValueKind == JsonValueKind.Object
            ? creatorEl
            : default;

        return new BandLabResolvedTrack(
            $"bandlab:{postId}",
            sourceUri.AbsoluteUri,
            NormalizeUrl(audioUrl, sourceUri),
            FirstNonEmpty(
                TryGetString(song, "name", "title"),
                TryGetNestedString(postElement, "revision", "name"),
                TryGetString(postElement, "title", "name", "caption"),
                TryGetNestedString(postElement, "song", "name")),
            FirstNonEmpty(
                TryGetString(creator, "name"),
                TryGetString(creator, "username"),
                TryGetNestedString(postElement, "creator", "name"),
                TryGetNestedString(postElement, "creator", "username"),
                TryGetNestedString(postElement, "user", "name"),
                TryGetNestedString(postElement, "owner", "name")),
            FirstNonEmpty(TryGetString(song, "name"), "BandLab"),
            TryGetGenre(revision),
            TryGetNestedDouble(revision, "mixdown", "duration"),
            TryGetNestedString(revision, "lyrics", "content"),
            NormalizeOptionalUrl(GetSizedPictureUrl(song, "640x640"), sourceUri));
    }

    private static bool IsBandLabTrackUri(Uri uri)
    {
        if (uri.Scheme is not ("http" or "https") || !IsBandLabHost(uri.Host))
        {
            return false;
        }

        var parts = GetPathParts(uri);
        return parts.Length switch
        {
            >= 2 when parts[0].Equals("track", StringComparison.OrdinalIgnoreCase) => true,
            >= 2 when parts[0].Equals("revisions", StringComparison.OrdinalIgnoreCase) => true,
            >= 2 when IsReservedPath(parts[0]) => false,
            >= 2 => true,
            _ => false,
        };
    }

    private static bool IsBandLabHost(string host) =>
        host.Equals("bandlab.com", StringComparison.OrdinalIgnoreCase) ||
        host.Equals("www.bandlab.com", StringComparison.OrdinalIgnoreCase);

    private static string[] GetPathParts(Uri uri) =>
        uri.AbsolutePath
            .Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Select(Uri.UnescapeDataString)
            .ToArray();

    private static bool IsReservedPath(string path) =>
        path.Equals("api", StringComparison.OrdinalIgnoreCase) ||
        path.Equals("feed", StringComparison.OrdinalIgnoreCase) ||
        path.Equals("discover", StringComparison.OrdinalIgnoreCase) ||
        path.Equals("library", StringComparison.OrdinalIgnoreCase) ||
        path.Equals("sounds", StringComparison.OrdinalIgnoreCase) ||
        path.Equals("albums", StringComparison.OrdinalIgnoreCase);

    private static bool TryGetPostId(Uri uri, out string postId)
    {
        var parts = GetPathParts(uri);
        if (parts.Length >= 2 && parts[0].Equals("track", StringComparison.OrdinalIgnoreCase))
        {
            postId = parts[1];
            return !string.IsNullOrWhiteSpace(postId);
        }

        postId = "";
        return false;
    }

    private static bool TryGetRevisionSharePostId(Uri uri, out string postId)
    {
        var parts = GetPathParts(uri);
        if (parts.Length >= 2 && parts[0].Equals("revisions", StringComparison.OrdinalIgnoreCase))
        {
            postId = parts[1];
            return !string.IsNullOrWhiteSpace(postId);
        }

        postId = "";
        return false;
    }

    private static string? GetSharedKey(Uri uri)
    {
        foreach (var part in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var separatorIndex = part.IndexOf('=');
            var name = separatorIndex >= 0 ? part[..separatorIndex] : part;
            if (!Uri.UnescapeDataString(name).Equals("sharedKey", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var value = separatorIndex >= 0 ? part[(separatorIndex + 1)..] : "";
            return Uri.UnescapeDataString(value.Replace("+", " "));
        }

        return null;
    }

    private static bool TryFindPlayablePost(
        JsonElement element,
        out JsonElement postElement,
        out string audioUrl)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var candidateUrl = TryGetMixdownFile(element);
            if (!string.IsNullOrWhiteSpace(candidateUrl))
            {
                postElement = element;
                audioUrl = candidateUrl;
                return true;
            }

            foreach (var property in element.EnumerateObject())
            {
                if (TryFindPlayablePost(property.Value, out postElement, out audioUrl))
                {
                    return true;
                }
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                if (TryFindPlayablePost(item, out postElement, out audioUrl))
                {
                    return true;
                }
            }
        }

        postElement = default;
        audioUrl = "";
        return false;
    }

    private static string? TryGetMixdownFile(JsonElement element)
    {
        if (!TryGetProperty(element, "revision", out var revision) ||
            revision.ValueKind != JsonValueKind.Object ||
            !TryGetProperty(revision, "mixdown", out var mixdown) ||
            mixdown.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        return TryGetString(mixdown, "file");
    }

    private static IEnumerable<string> EnumerateJsonCandidates(string html)
    {
        foreach (var scriptJson in EnumerateJsonScriptContents(html))
        {
            yield return scriptJson;
        }

        foreach (var marker in new[] { "__bl_bootstrap", "__BL_BOOTSTRAP__", "__NEXT_DATA__", "window.__" })
        {
            var searchIndex = 0;
            while (searchIndex < html.Length)
            {
                var markerIndex = html.IndexOf(marker, searchIndex, StringComparison.Ordinal);
                if (markerIndex < 0)
                {
                    break;
                }

                var startIndex = html.IndexOf('{', markerIndex + marker.Length);
                if (startIndex < 0)
                {
                    break;
                }

                var json = ExtractBalancedJsonObject(html, startIndex);
                if (!string.IsNullOrWhiteSpace(json))
                {
                    yield return WebUtility.HtmlDecode(json);
                }

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
            {
                yield return json;
            }
        }
    }

    private static string? TryExtractStaticAudioUrl(string html)
    {
        var match = Regex.Match(
            html,
            """https:\\?/\\?/static\.bandlab\.com\\?/revisions-formatted\\?/[^"'\\<>\s]+?\.m4a""",
            RegexOptions.IgnoreCase);
        if (!match.Success)
        {
            return null;
        }

        return match.Value
            .Replace(@"\/", "/", StringComparison.Ordinal)
            .Replace(@"\u002F", "/", StringComparison.OrdinalIgnoreCase);
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

    private static bool TryGetProperty(JsonElement element, string propertyName, out JsonElement value)
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

    private static string? TryGetString(JsonElement element, params string[] propertyNames)
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

    private static string? TryGetNestedString(JsonElement element, string objectName, string propertyName) =>
        TryGetProperty(element, objectName, out var nested) && nested.ValueKind == JsonValueKind.Object
            ? TryGetString(nested, propertyName)
            : null;

    private static double? TryGetDouble(JsonElement element, string propertyName)
    {
        if (!TryGetProperty(element, propertyName, out var value) ||
            value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return null;
        }

        return value.TryGetDouble(out var result) ? result : null;
    }

    private static double? TryGetNestedDouble(JsonElement element, string objectName, string propertyName) =>
        TryGetProperty(element, objectName, out var nested) && nested.ValueKind == JsonValueKind.Object
            ? TryGetDouble(nested, propertyName)
            : null;

    private static string? TryGetGenre(JsonElement revision)
    {
        if (!TryGetProperty(revision, "genres", out var genres) ||
            genres.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        foreach (var genre in genres.EnumerateArray())
        {
            var name = TryGetString(genre, "name");
            if (!string.IsNullOrWhiteSpace(name))
            {
                return name;
            }
        }

        return null;
    }

    private static string? GetSizedPictureUrl(JsonElement song, string size)
    {
        if (!TryGetProperty(song, "picture", out var picture) ||
            picture.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var baseUrl = TryGetString(picture, "url");
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            return null;
        }

        return baseUrl.EndsWith("/", StringComparison.Ordinal)
            ? baseUrl + size
            : baseUrl;
    }

    private static string NormalizeUrl(string value, Uri pageUri)
    {
        return Uri.TryCreate(value, UriKind.Absolute, out var absolute)
            ? absolute.AbsoluteUri
            : new Uri(pageUri, value).AbsoluteUri;
    }

    private static string? NormalizeOptionalUrl(string? value, Uri pageUri) =>
        string.IsNullOrWhiteSpace(value) ? null : NormalizeUrl(value, pageUri);

    private static string GetAudioExtension(string audioUrl)
    {
        if (Uri.TryCreate(audioUrl, UriKind.Absolute, out var uri))
        {
            var extension = Path.GetExtension(uri.AbsolutePath);
            if (!string.IsNullOrWhiteSpace(extension))
            {
                return extension;
            }
        }

        return ".m4a";
    }

    private static string BuildSourceKey(Uri uri)
    {
        var builder = new UriBuilder(uri) { Query = "", Fragment = "" };
        return builder.Uri.AbsolutePath.Trim('/').Replace('/', ':');
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

    private static void Log(string message)
    {
        try
        {
            AppLogPaths.AppendTimestamped(BandLabLogPath, message);
        }
        catch
        {
        }
    }

    private static HttpClient CreateHttpClient()
    {
        var http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd(
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36");
        http.DefaultRequestHeaders.Accept.ParseAdd("application/json,text/html,application/xhtml+xml,*/*");
        http.DefaultRequestHeaders.Referrer = new Uri("https://www.bandlab.com/");
        return http;
    }

    private sealed record BandLabResolvedTrack(
        string SourceId,
        string SourceUrl,
        string AudioUrl,
        string? Title,
        string? Artist,
        string? Album,
        string? Genre,
        double? DurationSeconds,
        string? LyricsText,
        string? ArtworkUrl);
}
