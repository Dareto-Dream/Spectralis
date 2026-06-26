using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;

namespace Spectralis;

internal static class EmbeddedLyricsDataReader
{
    public static string? TryExtractStructuredLyricsText(EmbeddedVisualizerContext? context)
    {
        if (context is null)
        {
            return null;
        }

        var seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var candidate in GetLyricsCandidates(context, seenIds))
        {
            var lyricsText = TryExtractLyricsText(candidate);
            if (!string.IsNullOrWhiteSpace(lyricsText))
            {
                return lyricsText;
            }
        }

        return null;
    }

    private static IEnumerable<EmbeddedDataBlock> GetLyricsCandidates(
        EmbeddedVisualizerContext context,
        ISet<string> seenIds)
    {
        foreach (var dataRef in context.Module.DataRefs)
        {
            if (!LooksLikeLyricsReference(dataRef.Key) && !LooksLikeLyricsReference(dataRef.Value))
            {
                continue;
            }

            var dataBlock = context.GetDataByReference(dataRef.Value);
            if (dataBlock is not null && seenIds.Add(dataBlock.Id))
            {
                yield return dataBlock;
            }
        }

        foreach (var dataBlock in context.DataBlocks.Values)
        {
            if (LooksLikeLyricsReference(dataBlock.Id) && seenIds.Add(dataBlock.Id))
            {
                yield return dataBlock;
            }
        }
    }

    private static string? TryExtractLyricsText(EmbeddedDataBlock dataBlock)
    {
        if (LooksLikeLrcText(dataBlock.RawText))
        {
            return dataBlock.RawText.Trim();
        }

        if (dataBlock.JsonValue is JsonObject jsonObject)
        {
            var inlineText = dataBlock.TryGetString("lrc", "lyrics", "text", "content");
            if (LooksLikeLrcText(inlineText))
            {
                return inlineText;
            }

            if (jsonObject["lines"] is JsonArray lines &&
                TryConvertJsonLinesToLrc(lines, out var lrcText))
            {
                return lrcText;
            }
        }

        if (dataBlock.JsonValue is JsonArray jsonArray &&
            TryConvertJsonLinesToLrc(jsonArray, out var arrayLrc))
        {
            return arrayLrc;
        }

        return null;
    }

    private static bool TryConvertJsonLinesToLrc(JsonArray lines, out string? lrcText)
    {
        var builder = new StringBuilder();

        foreach (var lineNode in lines)
        {
            if (lineNode is not JsonObject lineObject ||
                !TryReadLineText(lineObject, out var text) ||
                !TryReadLineTime(lineObject, out var timeSeconds))
            {
                continue;
            }

            builder.Append('[');
            builder.Append(FormatLrcTimestamp(timeSeconds));
            builder.Append(']');
            builder.AppendLine(text);
        }

        lrcText = builder.Length == 0 ? null : builder.ToString().Trim();
        return !string.IsNullOrWhiteSpace(lrcText);
    }

    private static bool TryReadLineText(JsonObject lineObject, out string? text)
    {
        text = null;

        foreach (var propertyName in new[] { "text", "value", "line", "lyrics" })
        {
            if (lineObject[propertyName] is JsonValue jsonValue &&
                jsonValue.TryGetValue<string>(out var stringValue) &&
                !string.IsNullOrWhiteSpace(stringValue))
            {
                text = stringValue.Trim();
                return true;
            }
        }

        return false;
    }

    private static bool TryReadLineTime(JsonObject lineObject, out double timeSeconds)
    {
        foreach (var propertyName in new[] { "time", "timestamp", "start", "startTime" })
        {
            if (lineObject[propertyName] is not JsonValue jsonValue)
            {
                continue;
            }

            if (jsonValue.TryGetValue<double>(out timeSeconds))
            {
                return true;
            }

            if (jsonValue.TryGetValue<string>(out var stringValue) &&
                double.TryParse(stringValue, CultureInfo.InvariantCulture, out timeSeconds))
            {
                return true;
            }
        }

        timeSeconds = 0;
        return false;
    }

    private static string FormatLrcTimestamp(double timeSeconds)
    {
        var safeTime = Math.Max(0, timeSeconds);
        var totalCentiseconds = (int)Math.Round(safeTime * 100, MidpointRounding.AwayFromZero);
        var minutes = totalCentiseconds / 6000;
        var seconds = (totalCentiseconds / 100) % 60;
        var centiseconds = totalCentiseconds % 100;
        return $"{minutes:00}:{seconds:00}.{centiseconds:00}";
    }

    private static bool LooksLikeLyricsReference(string value) =>
        value.Contains("lyric", StringComparison.OrdinalIgnoreCase) ||
        value.Contains("lrc", StringComparison.OrdinalIgnoreCase);

    private static bool LooksLikeLrcText(string? value) =>
        !string.IsNullOrWhiteSpace(value) &&
        value.Contains('[', StringComparison.Ordinal) &&
        value.Contains(':', StringComparison.Ordinal);
}
