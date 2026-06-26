using System.Globalization;
using System.Text.Json;

namespace Spectralis;

internal static class LyricsExplanationParser
{
    public static Dictionary<string, string> Parse(string? jsonText)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);

        if (string.IsNullOrWhiteSpace(jsonText))
        {
            return result;
        }

        try
        {
            using var doc = JsonDocument.Parse(jsonText);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
            {
                return result;
            }

            foreach (var property in doc.RootElement.EnumerateObject())
            {
                if (property.Value.ValueKind == JsonValueKind.String)
                {
                    var explanation = property.Value.GetString();
                    if (!string.IsNullOrWhiteSpace(explanation))
                    {
                        result[property.Name] = explanation;
                    }
                }
            }
        }
        catch
        {
            return result;
        }

        return result;
    }

    public static string? GetExplanationForTimestamp(
        IReadOnlyDictionary<string, string> explanations,
        double startTimeSeconds)
    {
        var timeSpan = TimeSpan.FromSeconds(startTimeSeconds);
        var key = FormatTimestamp(timeSpan);

        if (explanations.TryGetValue(key, out var explanation))
        {
            return explanation;
        }

        return null;
    }

    private static string FormatTimestamp(TimeSpan time)
    {
        var totalCentiseconds = (long)Math.Round(time.TotalMilliseconds / 10d, MidpointRounding.AwayFromZero);
        var minutes = totalCentiseconds / 6000;
        var seconds = (totalCentiseconds / 100) % 60;
        var centiseconds = totalCentiseconds % 100;
        return $"{minutes:D2}:{seconds:D2}.{centiseconds:D2}";
    }
}
