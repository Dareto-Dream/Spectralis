using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Spectralis.App.Services;

internal sealed record SunoClipInfo(
    string Id,
    string Title,
    string? Artist,
    string? Tags,
    string AudioUrl,
    string? ImageUrl,
    string? ImageLargeUrl,
    string? LyricsText,
    double? DurationSeconds);

internal static class SunoClipResolver
{
    // Suno's studio API now returns audio through a flat, unauthenticated CloudFront
    // path derived from the clip id alone: https://<host>/1/clip/{id}.m4a (m4a-opus).
    // The old cdn1.suno.ai/{id}.mp3 path is gone.
    private const string AudioCdnHost = "d2lwuy8qc234o3.cloudfront.net";

    private static string BuildCdnAudioUrl(string clipId) =>
        $"https://{AudioCdnHost}/1/clip/{clipId}.m4a";

    private static readonly Regex ClipIdRegex = new(
        @"(?i)\b[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex NextFlightChunkRegex = new(
        @"<script>\s*self\.__next_f\.push\(\[1,""(?<payload>(?:\\.|[^""\\])*)""\]\)\s*</script>",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex TextReferenceMarkerRegex = new(
        @"^(?<id>[0-9a-z]+):T[0-9a-f]+,$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    private static readonly HttpClient Http = CreateHttpClient();

    public static string SunoLogPath => AppLogPaths.For("suno.log");

    public static bool TryExtractClipId(string input, out string clipId)
    {
        var match = ClipIdRegex.Match(input ?? string.Empty);
        clipId = match.Success ? match.Value.ToLowerInvariant() : string.Empty;
        return match.Success;
    }

    public static async Task<SunoClipInfo> ResolveAsync(string input, CancellationToken cancellationToken)
    {
        if (!TryExtractClipId(input, out var clipId))
        {
            throw new FormatException("Paste a Suno song URL, embed URL, CDN URL, or clip ID.");
        }

        LogSuno($"Resolve: {clipId}");
        var fallback = CreateFallback(clipId);
        foreach (var pageUrl in BuildPublicPageUrls(clipId, input))
        {
            try
            {
                var html = await Http.GetStringAsync(pageUrl, cancellationToken);
                var parsed = TryParsePublicPage(html, clipId);
                if (parsed is not null)
                {
                    return parsed;
                }

                var metaFallback = TryBuildFromOpenGraph(html, fallback);
                if (metaFallback is not null)
                {
                    return metaFallback;
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LogSuno($"Metadata resolve failed for {pageUrl}: {ex.Message}");
            }
        }

        return fallback;
    }

    private static HttpClient CreateHttpClient()
    {
        var http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd(
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36");
        http.DefaultRequestHeaders.Accept.ParseAdd("text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8");
        return http;
    }

    private static IEnumerable<string> BuildPublicPageUrls(string clipId, string originalInput)
    {
        if (Uri.TryCreate(originalInput, UriKind.Absolute, out var uri) &&
            uri.Host.EndsWith("suno.com", StringComparison.OrdinalIgnoreCase))
        {
            yield return uri.ToString();
        }

        yield return $"https://suno.com/embed/{clipId}";
        yield return $"https://suno.com/song/{clipId}";
    }

    private static SunoClipInfo CreateFallback(string clipId) =>
        new(
            clipId,
            $"Suno {clipId[..8]}",
            "Suno",
            null,
            BuildCdnAudioUrl(clipId),
            $"https://cdn2.suno.ai/image_{clipId}.jpeg",
            $"https://cdn2.suno.ai/image_large_{clipId}.jpeg",
            null,
            null);

    private static SunoClipInfo? TryParsePublicPage(string html, string requestedClipId)
    {
        var chunks = ExtractNextFlightChunks(html);
        if (chunks.Count == 0)
        {
            return null;
        }

        var textReferences = ExtractTextReferences(chunks);
        var flightText = string.Concat(chunks);
        var clipJson = TryExtractClipJson(flightText, requestedClipId);
        if (clipJson is null)
        {
            return null;
        }

        SunoClipJson? clip;
        try
        {
            clip = JsonSerializer.Deserialize<SunoClipJson>(clipJson);
        }
        catch
        {
            return null;
        }

        if (clip is null || string.IsNullOrWhiteSpace(clip.Id))
        {
            return null;
        }

        var id = clip.Id.Trim().ToLowerInvariant();
        if (!string.Equals(id, requestedClipId, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var prompt = ResolveTextReference(clip.Metadata?.Prompt, textReferences);
        var title = FirstNonEmpty(clip.Title, $"Suno {id[..8]}")!;
        var artist = FirstNonEmpty(clip.DisplayName, clip.Handle, "Suno");
        var audioUrl = ResolveAudioUrl(clip, id);

        return new SunoClipInfo(
            id,
            title,
            artist,
            clip.Metadata?.Tags,
            audioUrl,
            FirstNonEmpty(clip.ImageUrl, $"https://cdn2.suno.ai/image_{id}.jpeg"),
            FirstNonEmpty(clip.ImageLargeUrl, $"https://cdn2.suno.ai/image_large_{id}.jpeg"),
            prompt,
            clip.Metadata?.Duration);
    }

    private static IReadOnlyList<string> ExtractNextFlightChunks(string html)
    {
        var chunks = new List<string>();
        foreach (Match match in NextFlightChunkRegex.Matches(html))
        {
            var payload = match.Groups["payload"].Value;
            try
            {
                var decoded = JsonSerializer.Deserialize<string>($"\"{payload}\"");
                if (decoded is not null)
                {
                    chunks.Add(WebUtility.HtmlDecode(decoded));
                }
            }
            catch
            {
                // Ignore malformed chunks; a later chunk may still contain the clip.
            }
        }

        return chunks;
    }

    private static Dictionary<string, string> ExtractTextReferences(IReadOnlyList<string> chunks)
    {
        var references = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var i = 0; i < chunks.Count - 1; i++)
        {
            var marker = chunks[i].Trim();
            var match = TextReferenceMarkerRegex.Match(marker);
            if (!match.Success)
            {
                continue;
            }

            references[$"${match.Groups["id"].Value}"] = chunks[i + 1];
        }

        return references;
    }

    private static string? TryExtractClipJson(string flightText, string requestedClipId)
    {
        // Legacy shape: the page embedded a bare {"clip": { ... }} node.
        var keyIndex = flightText.IndexOf("\"clip\":", StringComparison.Ordinal);
        if (keyIndex >= 0)
        {
            var objectStart = flightText.IndexOf('{', keyIndex);
            if (objectStart >= 0 &&
                TryExtractBalancedJsonObject(flightText, objectStart) is { } legacy &&
                legacy.Contains(requestedClipId, StringComparison.OrdinalIgnoreCase))
            {
                return legacy;
            }
        }

        // Current shape: the song lives inside a content_item tree as a
        // {"id":"<uuid>", "entity_type":"song_schema", ...} object. Anchor on the
        // clip id and pull out the innermost JSON object that encloses it.
        foreach (var idNeedle in new[] { $"\"id\":\"{requestedClipId}\"", $"\"id\": \"{requestedClipId}\"" })
        {
            var idIndex = flightText.IndexOf(idNeedle, StringComparison.OrdinalIgnoreCase);
            while (idIndex >= 0)
            {
                var enclosing = TryExtractEnclosingJsonObject(flightText, idIndex);
                if (enclosing is not null && LooksLikeSongObject(enclosing))
                {
                    return enclosing;
                }

                idIndex = flightText.IndexOf(idNeedle, idIndex + idNeedle.Length, StringComparison.OrdinalIgnoreCase);
            }
        }

        return null;
    }

    private static bool LooksLikeSongObject(string json) =>
        json.Contains("\"entity_type\"", StringComparison.Ordinal) ||
        json.Contains("\"media_urls\"", StringComparison.Ordinal) ||
        json.Contains("\"metadata\"", StringComparison.Ordinal);

    // Forward, string-aware pass that returns the innermost balanced { } object
    // whose span contains needleIndex.
    private static string? TryExtractEnclosingJsonObject(string text, int needleIndex)
    {
        var starts = new Stack<int>();
        var inString = false;
        var escaped = false;

        for (var i = 0; i < text.Length; i++)
        {
            var ch = text[i];
            if (inString)
            {
                if (escaped) escaped = false;
                else if (ch == '\\') escaped = true;
                else if (ch == '"') inString = false;
                continue;
            }

            switch (ch)
            {
                case '"':
                    inString = true;
                    break;
                case '{':
                    starts.Push(i);
                    break;
                case '}' when starts.Count > 0:
                    var start = starts.Pop();
                    if (start <= needleIndex && needleIndex <= i)
                    {
                        // Inner objects close before their parents, so the first
                        // closing brace past the needle is its innermost enclosure.
                        return text[start..(i + 1)];
                    }

                    break;
            }
        }

        return null;
    }

    private static string ResolveAudioUrl(SunoClipJson clip, string clipId)
    {
        var fromMedia = clip.MediaUrls?
            .Select(static m => m.Url)
            .FirstOrDefault(IsUsableAudioUrl);
        if (fromMedia is not null)
        {
            return fromMedia.Trim();
        }

        // audio_url is now a decoy field that always resolves to /api/forbidden on
        // the studio API host. Only trust it when it points at a real CDN.
        return IsUsableAudioUrl(clip.AudioUrl)
            ? clip.AudioUrl!.Trim()
            : BuildCdnAudioUrl(clipId);
    }

    private static bool IsUsableAudioUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url) ||
            !Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri) ||
            uri.Scheme != Uri.UriSchemeHttps)
        {
            return false;
        }

        if (uri.AbsolutePath.Contains("forbidden", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        // studio-api-prod.suno.com / studio-api.prod.suno.com only gate discovery,
        // never delivery — an audio URL on that host is the decoy.
        return !uri.Host.Contains("studio-api", StringComparison.OrdinalIgnoreCase) &&
               !uri.Host.Contains("api.prod.suno", StringComparison.OrdinalIgnoreCase);
    }

    private static string? TryExtractBalancedJsonObject(string text, int objectStart)
    {
        var depth = 0;
        var inString = false;
        var escaped = false;

        for (var i = objectStart; i < text.Length; i++)
        {
            var ch = text[i];
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
            }
            else if (ch == '}')
            {
                depth--;
                if (depth == 0)
                {
                    return text[objectStart..(i + 1)];
                }
            }
        }

        return null;
    }

    private static string? ResolveTextReference(string? value, IReadOnlyDictionary<string, string> references)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return references.TryGetValue(value, out var referencedText)
            ? referencedText
            : value;
    }

    private static SunoClipInfo? TryBuildFromOpenGraph(string html, SunoClipInfo fallback)
    {
        var title = ExtractMetaContent(html, "og:title") ?? fallback.Title;
        var image = ExtractMetaContent(html, "og:image") ?? fallback.ImageLargeUrl ?? fallback.ImageUrl;
        return new SunoClipInfo(
            fallback.Id,
            title,
            fallback.Artist,
            fallback.Tags,
            fallback.AudioUrl,
            fallback.ImageUrl,
            image,
            fallback.LyricsText,
            fallback.DurationSeconds);
    }

    private static string? ExtractMetaContent(string html, string name)
    {
        var escapedName = Regex.Escape(name);
        var patternA = $@"<meta\b(?=[^>]*(?:property|name)\s*=\s*[""']{escapedName}[""'])(?=[^>]*content\s*=\s*[""'](?<content>[^""']*)[""'])[^>]*>";
        var patternB = $@"<meta\b(?=[^>]*content\s*=\s*[""'](?<content>[^""']*)[""'])(?=[^>]*(?:property|name)\s*=\s*[""']{escapedName}[""'])[^>]*>";
        var match = Regex.Match(html, patternA, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)
            .Success
            ? Regex.Match(html, patternA, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)
            : Regex.Match(html, patternB, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        return match.Success
            ? WebUtility.HtmlDecode(match.Groups["content"].Value)
            : null;
    }

    private static string? FirstNonEmpty(params string?[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value.Trim();
            }
        }

        return null;
    }

    private static void LogSuno(string message)
    {
        try
        {
            AppLogPaths.AppendTimestamped(SunoLogPath, message);
        }
        catch
        {
        }
    }

    private sealed class SunoClipJson
    {
        [JsonPropertyName("id")]
        public string? Id { get; set; }

        [JsonPropertyName("title")]
        public string? Title { get; set; }

        [JsonPropertyName("display_name")]
        public string? DisplayName { get; set; }

        [JsonPropertyName("handle")]
        public string? Handle { get; set; }

        // Decoy field: always resolves to /api/forbidden on the studio API host.
        // Kept only so IsUsableAudioUrl can reject it explicitly.
        [JsonPropertyName("audio_url")]
        public string? AudioUrl { get; set; }

        // Real playback source(s). media_urls[0].url is a flat, unauthenticated
        // CloudFront path (content_type "m4a-opus", progressive delivery).
        [JsonPropertyName("media_urls")]
        public List<SunoMediaUrlJson>? MediaUrls { get; set; }

        [JsonPropertyName("image_url")]
        public string? ImageUrl { get; set; }

        [JsonPropertyName("image_large_url")]
        public string? ImageLargeUrl { get; set; }

        [JsonPropertyName("metadata")]
        public SunoClipMetadataJson? Metadata { get; set; }
    }

    private sealed class SunoMediaUrlJson
    {
        [JsonPropertyName("url")]
        public string? Url { get; set; }

        [JsonPropertyName("content_type")]
        public string? ContentType { get; set; }
    }

    private sealed class SunoClipMetadataJson
    {
        [JsonPropertyName("tags")]
        public string? Tags { get; set; }

        [JsonPropertyName("prompt")]
        public string? Prompt { get; set; }

        [JsonPropertyName("duration")]
        public double? Duration { get; set; }
    }
}
