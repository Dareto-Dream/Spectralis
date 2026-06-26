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
            $"https://cdn1.suno.ai/{clipId}.mp3",
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
        var clipJson = TryExtractClipJson(flightText);
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
        var audioUrl = FirstNonEmpty(clip.AudioUrl, $"https://cdn1.suno.ai/{id}.mp3")!;

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

    private static string? TryExtractClipJson(string flightText)
    {
        var keyIndex = flightText.IndexOf("\"clip\":", StringComparison.Ordinal);
        if (keyIndex < 0)
        {
            return null;
        }

        var objectStart = flightText.IndexOf('{', keyIndex);
        return objectStart < 0 ? null : TryExtractBalancedJsonObject(flightText, objectStart);
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

        [JsonPropertyName("audio_url")]
        public string? AudioUrl { get; set; }

        [JsonPropertyName("image_url")]
        public string? ImageUrl { get; set; }

        [JsonPropertyName("image_large_url")]
        public string? ImageLargeUrl { get; set; }

        [JsonPropertyName("metadata")]
        public SunoClipMetadataJson? Metadata { get; set; }
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
