using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Spectralis.App.Services;

internal static class SoundCloudNativeResolver
{
    private static readonly HttpClient Http = CreateHttpClient();
    private static readonly SemaphoreSlim ClientIdLock = new(1, 1);
    private static string? cachedClientId;

    public static string SoundCloudLogPath => AppLogPaths.For("soundcloud.log");

    public static async Task<RemoteAudioResolveResult> ResolveAsync(
        Uri uri,
        CancellationToken cancellationToken)
    {
        Log($"Resolve: {uri.AbsoluteUri}");
        var playbackSource = await ResolvePlaybackSourceAsync(uri, cancellationToken);
        var nativeTrack = await ResolveNativeTrackAsync(playbackSource, cancellationToken) ??
            throw new InvalidOperationException(
                "SoundCloud did not expose a native progressive MP3 stream for this track.");

        return new RemoteAudioResolveResult(
            RemoteAudioServiceKind.SoundCloud,
            "SoundCloud",
            playbackSource.Url,
            nativeTrack.StreamUrl,
            ".mp3",
            "SoundCloud MP3",
            nativeTrack.Title,
            nativeTrack.Artist,
            FirstNonEmpty(nativeTrack.Genre, "SoundCloud"),
            NormalizeArtworkUrl(nativeTrack.ArtworkUrl),
            nativeTrack.DurationMs > 0 ? TimeSpan.FromMilliseconds(nativeTrack.DurationMs) : TimeSpan.Zero,
            playbackSource.Url,
            LyricsText: nativeTrack.Description);
    }

    private static async Task<SoundCloudPlaybackSource> ResolvePlaybackSourceAsync(
        Uri sourceUri,
        CancellationToken cancellationToken)
    {
        var uri = sourceUri;
        if (ShouldResolveRedirect(uri))
        {
            try
            {
                using var handler = new HttpClientHandler { AllowAutoRedirect = false };
                using var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(10) };

                for (var redirect = 0; redirect < 8; redirect++)
                {
                    using var request = new HttpRequestMessage(HttpMethod.Get, uri);
                    request.Headers.UserAgent.ParseAdd(
                        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36");
                    request.Headers.Accept.ParseAdd("text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8");

                    using var response = await http.SendAsync(
                        request,
                        HttpCompletionOption.ResponseHeadersRead,
                        cancellationToken);
                    if (!IsRedirectStatusCode(response.StatusCode) || response.Headers.Location is null)
                    {
                        Log($"Short-link resolve stopped with HTTP {(int)response.StatusCode}");
                        break;
                    }

                    var nextUri = response.Headers.Location.IsAbsoluteUri
                        ? response.Headers.Location
                        : new Uri(uri, response.Headers.Location);

                    Log($"Resolved redirect {redirect + 1}: {uri} -> {nextUri}");
                    if (!IsSoundCloudHost(nextUri))
                    {
                        break;
                    }

                    uri = nextUri;
                    if (!ShouldResolveRedirect(uri))
                    {
                        break;
                    }
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                Log($"Short-link resolve failed: {ex.Message}");
                uri = sourceUri;
            }
        }

        var playbackUrl = NormalizePlaybackUrl(uri);
        return await ResolveOEmbedSourceAsync(playbackUrl, cancellationToken) ??
            new SoundCloudPlaybackSource(playbackUrl, null);
    }

    private static async Task<SoundCloudPlaybackSource?> ResolveOEmbedSourceAsync(
        string url,
        CancellationToken cancellationToken)
    {
        try
        {
            var oEmbedUrl = $"https://soundcloud.com/oembed?format=json&url={Uri.EscapeDataString(url)}";
            var json = await Http.GetStringAsync(oEmbedUrl, cancellationToken);
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("html", out var htmlElement))
            {
                return null;
            }

            return TryExtractWidgetSource(htmlElement.GetString());
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Log($"oEmbed resolve failed: {ex.Message}");
            return null;
        }
    }

    private static SoundCloudPlaybackSource? TryExtractWidgetSource(string? html)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return null;
        }

        var match = Regex.Match(html, """src\s*=\s*["'](?<src>[^"']+)["']""", RegexOptions.IgnoreCase);
        if (!match.Success)
        {
            return null;
        }

        var source = WebUtility.HtmlDecode(match.Groups["src"].Value);
        if (!Uri.TryCreate(source, UriKind.Absolute, out var widgetUri))
        {
            return null;
        }

        var query = ParseQueryParameters(widgetUri.Query);
        if (!query.TryGetValue("url", out var targetUrl) || string.IsNullOrWhiteSpace(targetUrl))
        {
            return null;
        }

        query.TryGetValue("secret_token", out var secretToken);
        return new SoundCloudPlaybackSource(
            targetUrl,
            string.IsNullOrWhiteSpace(secretToken) ? null : secretToken);
    }

    private static async Task<SoundCloudNativeTrack?> ResolveNativeTrackAsync(
        SoundCloudPlaybackSource playbackSource,
        CancellationToken cancellationToken)
    {
        var clientId = await GetClientIdAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(clientId))
        {
            Log("Native resolve skipped: no SoundCloud client_id");
            return null;
        }

        using var trackDoc = await FetchTrackJsonAsync(playbackSource, clientId, cancellationToken);
        if (trackDoc is null)
        {
            return null;
        }

        var root = trackDoc.RootElement;
        if (root.TryGetProperty("kind", out var kindEl) &&
            !string.Equals(kindEl.GetString(), "track", StringComparison.OrdinalIgnoreCase))
        {
            Log($"Native resolve skipped: unsupported kind {kindEl.GetString() ?? "(null)"}");
            return null;
        }

        if (root.TryGetProperty("streamable", out var streamableEl) &&
            streamableEl.ValueKind == JsonValueKind.False)
        {
            Log("Native resolve skipped: track is not streamable");
            return null;
        }

        var transcodingUrl = TryGetProgressiveTranscodingUrl(root);
        if (string.IsNullOrWhiteSpace(transcodingUrl))
        {
            Log("Native resolve skipped: no progressive MP3 transcoding");
            return null;
        }

        var streamUrl = await ResolveStreamUrlAsync(transcodingUrl, clientId, cancellationToken);
        if (string.IsNullOrWhiteSpace(streamUrl))
        {
            return null;
        }

        var id = TryGetJsonInt64(root, "id")?.ToString() ??
            TryExtractTrackId(playbackSource.Url) ??
            "track";
        var title = FirstNonEmpty(TryGetJsonString(root, "title"), "SoundCloud track")!;
        var artist = FirstNonEmpty(
            TryGetNestedJsonString(root, "publisher_metadata", "artist"),
            TryGetNestedJsonString(root, "user", "username"));
        var durationMs = TryGetJsonDouble(root, "full_duration") ??
            TryGetJsonDouble(root, "duration") ??
            0;

        var artworkUrl = TryGetJsonString(root, "artwork_url")
            ?? TryGetNestedJsonString(root, "publisher_metadata", "artwork_url")
            ?? TryGetNestedJsonString(root, "user", "avatar_url");

        return new SoundCloudNativeTrack(
            id,
            title,
            artist,
            TryGetJsonString(root, "genre"),
            artworkUrl,
            durationMs,
            streamUrl,
            TryGetJsonString(root, "description"));
    }

    private static async Task<JsonDocument?> FetchTrackJsonAsync(
        SoundCloudPlaybackSource playbackSource,
        string clientId,
        CancellationToken cancellationToken)
    {
        var trackId = TryExtractTrackId(playbackSource.Url);
        var apiUrl = trackId is not null
            ? AppendQueryParameter($"https://api-v2.soundcloud.com/tracks/{trackId}", "client_id", clientId)
            : AppendQueryParameter(
                "https://api-v2.soundcloud.com/resolve",
                "url",
                playbackSource.Url);

        if (trackId is not null && !string.IsNullOrWhiteSpace(playbackSource.SecretToken))
        {
            apiUrl = AppendQueryParameter(apiUrl, "secret_token", playbackSource.SecretToken);
        }

        if (trackId is null)
        {
            apiUrl = AppendQueryParameter(apiUrl, "client_id", clientId);
        }

        try
        {
            var json = await Http.GetStringAsync(apiUrl, cancellationToken);
            return JsonDocument.Parse(json);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Log($"Native track API failed: {ex.Message}");
            return null;
        }
    }

    private static async Task<string?> ResolveStreamUrlAsync(
        string transcodingUrl,
        string clientId,
        CancellationToken cancellationToken)
    {
        try
        {
            var url = AppendQueryParameter(transcodingUrl, "client_id", clientId);
            var json = await Http.GetStringAsync(url, cancellationToken);
            using var doc = JsonDocument.Parse(json);
            return TryGetJsonString(doc.RootElement, "url");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Log($"Native stream API failed: {ex.Message}");
            return null;
        }
    }

    private static async Task<string?> GetClientIdAsync(CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(cachedClientId))
        {
            return cachedClientId;
        }

        await ClientIdLock.WaitAsync(cancellationToken);
        try
        {
            if (!string.IsNullOrWhiteSpace(cachedClientId))
            {
                return cachedClientId;
            }

            var html = await Http.GetStringAsync("https://soundcloud.com/", cancellationToken);
            var assets = Regex.Matches(
                    html,
                    """https://a-v2\.sndcdn\.com/assets/[^"']+\.js""",
                    RegexOptions.IgnoreCase)
                .Cast<Match>()
                .Select(match => match.Value)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            foreach (var asset in assets)
            {
                string script;
                try
                {
                    script = await Http.GetStringAsync(asset, cancellationToken);
                }
                catch
                {
                    continue;
                }

                var match = Regex.Match(
                    script,
                    """client_id\s*:\s*["'](?<id>[A-Za-z0-9_-]{16,})["']""",
                    RegexOptions.IgnoreCase);
                if (!match.Success)
                {
                    continue;
                }

                cachedClientId = match.Groups["id"].Value;
                Log("Discovered SoundCloud client_id from web assets");
                return cachedClientId;
            }

            return null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Log($"SoundCloud client_id discovery failed: {ex.Message}");
            return null;
        }
        finally
        {
            ClientIdLock.Release();
        }
    }

    private static string? TryGetProgressiveTranscodingUrl(JsonElement root)
    {
        if (!root.TryGetProperty("media", out var mediaEl) ||
            !mediaEl.TryGetProperty("transcodings", out var transcodingsEl) ||
            transcodingsEl.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        foreach (var transcoding in transcodingsEl.EnumerateArray())
        {
            if (!transcoding.TryGetProperty("format", out var formatEl))
            {
                continue;
            }

            var protocol = TryGetJsonString(formatEl, "protocol");
            var mimeType = TryGetJsonString(formatEl, "mime_type");
            if (!string.Equals(protocol, "progressive", StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(mimeType, "audio/mpeg", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var url = TryGetJsonString(transcoding, "url");
            if (!string.IsNullOrWhiteSpace(url))
            {
                return url;
            }
        }

        return null;
    }

    private static bool ShouldResolveRedirect(Uri uri) =>
        uri.Scheme is "http" or "https" &&
        (uri.Host.Equals("on.soundcloud.com", StringComparison.OrdinalIgnoreCase) ||
         uri.Host.Equals("snd.sc", StringComparison.OrdinalIgnoreCase));

    private static bool IsSoundCloudHost(Uri uri) =>
        uri.Host.Equals("soundcloud.com", StringComparison.OrdinalIgnoreCase) ||
        uri.Host.EndsWith(".soundcloud.com", StringComparison.OrdinalIgnoreCase) ||
        uri.Host.Equals("snd.sc", StringComparison.OrdinalIgnoreCase);

    private static bool IsRedirectStatusCode(HttpStatusCode statusCode)
    {
        var code = (int)statusCode;
        return code is >= 300 and <= 399;
    }

    private static string NormalizePlaybackUrl(Uri uri)
    {
        if (!IsSoundCloudHost(uri) || string.IsNullOrEmpty(uri.Query))
        {
            return uri.ToString();
        }

        var queryParts = uri.Query.TrimStart('?')
            .Split('&', StringSplitOptions.RemoveEmptyEntries);
        var keptParts = queryParts
            .Where(part =>
            {
                var nameEnd = part.IndexOf('=');
                var encodedName = nameEnd >= 0 ? part[..nameEnd] : part;
                var name = Uri.UnescapeDataString(encodedName);
                return !name.Equals("si", StringComparison.OrdinalIgnoreCase) &&
                    !name.StartsWith("utm_", StringComparison.OrdinalIgnoreCase);
            })
            .ToArray();

        if (keptParts.Length == queryParts.Length)
        {
            return uri.ToString();
        }

        var builder = new UriBuilder(uri)
        {
            Query = string.Join("&", keptParts),
        };
        return builder.Uri.ToString();
    }

    private static Dictionary<string, string> ParseQueryParameters(string query)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var part in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var separatorIndex = part.IndexOf('=');
            var rawName = separatorIndex >= 0 ? part[..separatorIndex] : part;
            var rawValue = separatorIndex >= 0 ? part[(separatorIndex + 1)..] : "";
            var name = Uri.UnescapeDataString(rawName.Replace("+", " "));
            var value = Uri.UnescapeDataString(rawValue.Replace("+", " "));
            if (!string.IsNullOrWhiteSpace(name))
            {
                result[name] = value;
            }
        }

        return result;
    }

    private static string? TryExtractTrackId(string url)
    {
        var match = Regex.Match(
            url,
            @"/tracks/(?:soundcloud%3Atracks%3A)?(?<id>\d+)",
            RegexOptions.IgnoreCase);
        return match.Success ? match.Groups["id"].Value : null;
    }

    private static string? NormalizeArtworkUrl(string? artworkUrl) =>
        string.IsNullOrWhiteSpace(artworkUrl)
            ? null
            : artworkUrl.Replace("-large.", "-t500x500.", StringComparison.OrdinalIgnoreCase);

    private static string AppendQueryParameter(string url, string name, string value)
    {
        var separator = url.Contains('?') ? '&' : '?';
        return $"{url}{separator}{Uri.EscapeDataString(name)}={Uri.EscapeDataString(value)}";
    }

    private static string? TryGetJsonString(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var value) ||
            value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return null;
        }

        return value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : value.ToString();
    }

    private static string? TryGetNestedJsonString(JsonElement root, string objectName, string propertyName) =>
        root.TryGetProperty(objectName, out var objectEl) && objectEl.ValueKind == JsonValueKind.Object
            ? TryGetJsonString(objectEl, propertyName)
            : null;

    private static double? TryGetJsonDouble(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var value) ||
            value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return null;
        }

        return value.TryGetDouble(out var result) ? result : null;
    }

    private static long? TryGetJsonInt64(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var value) ||
            value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return null;
        }

        return value.TryGetInt64(out var result) ? result : null;
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
            AppLogPaths.AppendTimestamped(SoundCloudLogPath, message);
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
        return http;
    }

    private sealed record SoundCloudPlaybackSource(string Url, string? SecretToken);

    private sealed record SoundCloudNativeTrack(
        string Id,
        string Title,
        string? Artist,
        string? Genre,
        string? ArtworkUrl,
        double DurationMs,
        string StreamUrl,
        string? Description);
}
