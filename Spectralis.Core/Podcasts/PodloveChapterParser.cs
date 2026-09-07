using System.Globalization;
using System.Text.Json;

namespace Spectralis.Core.Podcasts;

/// <summary>
/// Parses a <c>.chapters.json</c> sidecar. Handles both the Podcasting 2.0 shape
/// (<c>startTime</c> as a number of seconds) and the Podlove Simple Chapters JSON shape
/// (<c>start</c> as <c>"HH:MM:SS.mmm"</c>).
/// </summary>
public static class PodloveChapterParser
{
    public static IReadOnlyList<Chapter> Parse(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            if (!document.RootElement.TryGetProperty("chapters", out var chaptersElement) ||
                chaptersElement.ValueKind != JsonValueKind.Array)
            {
                return Array.Empty<Chapter>();
            }

            var parsed = new List<(TimeSpan Start, string Title, string? Url)>();
            foreach (var entry in chaptersElement.EnumerateArray())
            {
                if (entry.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                if (!TryReadStart(entry, out var start))
                {
                    continue;
                }

                var title = TryGetString(entry, "title") ?? $"Chapter {parsed.Count + 1}";
                var url = TryGetString(entry, "url") ?? TryGetString(entry, "href");
                parsed.Add((start, title, string.IsNullOrWhiteSpace(url) ? null : url));
            }

            if (parsed.Count == 0)
            {
                return Array.Empty<Chapter>();
            }

            parsed.Sort((a, b) => a.Start.CompareTo(b.Start));
            var result = new List<Chapter>(parsed.Count);
            for (var i = 0; i < parsed.Count; i++)
            {
                TimeSpan? end = i + 1 < parsed.Count ? parsed[i + 1].Start : null;
                result.Add(new Chapter(parsed[i].Start, end, parsed[i].Title, parsed[i].Url));
            }

            return result;
        }
        catch (JsonException)
        {
            return Array.Empty<Chapter>();
        }
    }

    private static bool TryReadStart(JsonElement entry, out TimeSpan start)
    {
        start = default;

        foreach (var key in new[] { "startTime", "start" })
        {
            if (!entry.TryGetProperty(key, out var value))
            {
                continue;
            }

            switch (value.ValueKind)
            {
                case JsonValueKind.Number when value.TryGetDouble(out var seconds):
                    start = TimeSpan.FromSeconds(seconds);
                    return true;
                case JsonValueKind.String when TryParseTimecode(value.GetString(), out var parsed):
                    start = parsed;
                    return true;
            }
        }

        return false;
    }

    /// <summary>Parses "SS(.mmm)", "MM:SS(.mmm)" or "HH:MM:SS(.mmm)".</summary>
    private static bool TryParseTimecode(string? text, out TimeSpan value)
    {
        value = default;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var parts = text.Trim().Split(':');
        if (parts.Length is < 1 or > 3)
        {
            return false;
        }

        double total = 0;
        foreach (var part in parts)
        {
            if (!double.TryParse(part, NumberStyles.Float, CultureInfo.InvariantCulture, out var component))
            {
                return false;
            }

            total = (total * 60) + component;
        }

        value = TimeSpan.FromSeconds(total);
        return true;
    }

    private static string? TryGetString(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}
