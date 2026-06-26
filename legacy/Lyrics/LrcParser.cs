using System.Globalization;
using System.Text.RegularExpressions;

namespace Spectralis;

internal static partial class LrcParser
{
    private static readonly Regex InlineTimestampRegex = InlineTimestamp();

    public static LyricsDocument? Parse(string? rawText, string sourceLabel)
    {
        if (string.IsNullOrWhiteSpace(rawText))
        {
            return null;
        }

        var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var lines = new List<LyricsLine>();
        var normalizedText = rawText
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n');

        var offsetMilliseconds = 0;
        var isFirstLine = true;

        foreach (var rawLine in normalizedText.Split('\n'))
        {
            var line = isFirstLine
                ? rawLine.TrimStart('\uFEFF')
                : rawLine;

            isFirstLine = false;
            ParseLine(line.TrimEnd(), metadata, lines, ref offsetMilliseconds);
        }

        if (lines.Count == 0)
        {
            return null;
        }

        if (offsetMilliseconds != 0)
        {
            var deltaSeconds = offsetMilliseconds / 1000d;
            for (var i = 0; i < lines.Count; i++)
            {
                lines[i] = lines[i].ShiftedBy(deltaSeconds);
            }
        }

        return new LyricsDocument(lines, metadata, offsetMilliseconds, sourceLabel);
    }

    private static void ParseLine(
        string line,
        IDictionary<string, string> metadata,
        ICollection<LyricsLine> lines,
        ref int offsetMilliseconds)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return;
        }

        var timestamps = new List<double>();
        var cursor = 0;

        while (cursor < line.Length && line[cursor] == '[')
        {
            var closingBracket = line.IndexOf(']', cursor + 1);
            if (closingBracket < 0)
            {
                break;
            }

            var tagContent = line[(cursor + 1)..closingBracket].Trim();

            if (TryParseTimestamp(tagContent, out var timestampSeconds))
            {
                timestamps.Add(timestampSeconds);
                cursor = closingBracket + 1;
                continue;
            }

            if (timestamps.Count == 0 && TryParseMetadataTag(tagContent, out var key, out var value))
            {
                metadata[key] = value;

                if (key.Equals("offset", StringComparison.OrdinalIgnoreCase) &&
                    int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedOffset))
                {
                    offsetMilliseconds = parsedOffset;
                }

                return;
            }

            break;
        }

        if (timestamps.Count == 0)
        {
            return;
        }

        var lyricBody = cursor >= line.Length ? string.Empty : line[cursor..];
        var baseSegments = ParseSegments(lyricBody, timestamps[0]);
        var displayText = BuildDisplayText(baseSegments, lyricBody);

        foreach (var timestamp in timestamps)
        {
            var deltaSeconds = timestamp - timestamps[0];
            var shiftedSegments = deltaSeconds == 0
                ? baseSegments
                : baseSegments.Select(segment => segment.ShiftedBy(deltaSeconds)).ToArray();

            lines.Add(new LyricsLine(timestamp, displayText, shiftedSegments));
        }
    }

    private static LyricsSegment[] ParseSegments(string lyricBody, double fallbackStartTime)
    {
        if (string.IsNullOrEmpty(lyricBody))
        {
            return [];
        }

        var segments = new List<LyricsSegment>();
        var cursor = 0;
        double? pendingLeadingTime = null;
        var pendingLeadingPrefix = string.Empty;

        foreach (Match match in InlineTimestampRegex.Matches(lyricBody))
        {
            var betweenText = lyricBody[cursor..match.Index];
            if (!TryParseTimestamp(match.Groups["time"].Value, out var timestampSeconds))
            {
                cursor = match.Index + match.Length;
                continue;
            }

            if (betweenText.Length == 0)
            {
                pendingLeadingTime = timestampSeconds;
                pendingLeadingPrefix = string.Empty;
            }
            else if (pendingLeadingTime.HasValue)
            {
                var segmentText = pendingLeadingPrefix + betweenText;
                if (!string.IsNullOrEmpty(segmentText))
                {
                    segments.Add(new LyricsSegment(pendingLeadingTime.Value, segmentText));
                }

                pendingLeadingTime = timestampSeconds;
                pendingLeadingPrefix = string.Empty;
            }
            else if (ContainsRenderableText(betweenText))
            {
                segments.Add(new LyricsSegment(timestampSeconds, betweenText));
            }
            else
            {
                pendingLeadingTime = timestampSeconds;
                pendingLeadingPrefix = betweenText;
            }

            cursor = match.Index + match.Length;
        }

        var trailingText = lyricBody[cursor..];
        if (pendingLeadingTime.HasValue)
        {
            var segmentText = pendingLeadingPrefix + trailingText;
            if (!string.IsNullOrEmpty(segmentText))
            {
                segments.Add(new LyricsSegment(pendingLeadingTime.Value, segmentText));
            }
        }
        else if (segments.Count == 0)
        {
            segments.Add(new LyricsSegment(fallbackStartTime, lyricBody));
        }
        else if (!string.IsNullOrEmpty(trailingText))
        {
            var lastSegment = segments[^1];
            segments[^1] = lastSegment with { Text = lastSegment.Text + trailingText };
        }

        return NormalizeSegments(segments, lyricBody, fallbackStartTime);
    }

    private static LyricsSegment[] NormalizeSegments(
        IReadOnlyList<LyricsSegment> rawSegments,
        string fallbackText,
        double fallbackStartTime)
    {
        if (rawSegments.Count == 0)
        {
            var trimmedFallback = fallbackText.Trim();
            return trimmedFallback.Length == 0
                ? []
                : [new LyricsSegment(fallbackStartTime, trimmedFallback)];
        }

        var normalized = new List<LyricsSegment>(rawSegments.Count);

        for (var i = 0; i < rawSegments.Count; i++)
        {
            var text = rawSegments[i].Text;
            if (i == 0)
            {
                text = text.TrimStart();
            }

            if (i == rawSegments.Count - 1)
            {
                text = text.TrimEnd();
            }

            if (text.Length == 0)
            {
                continue;
            }

            normalized.Add(new LyricsSegment(rawSegments[i].StartTime, text));
        }

        if (normalized.Count == 0)
        {
            var trimmedFallback = fallbackText.Trim();
            return trimmedFallback.Length == 0
                ? []
                : [new LyricsSegment(fallbackStartTime, trimmedFallback)];
        }

        return normalized.ToArray();
    }

    private static string BuildDisplayText(IReadOnlyList<LyricsSegment> segments, string fallbackText)
    {
        if (segments.Count == 0)
        {
            return fallbackText.Trim();
        }

        return string.Concat(segments.Select(static segment => segment.Text)).Trim();
    }

    private static bool TryParseMetadataTag(string tagContent, out string key, out string value)
    {
        var separatorIndex = tagContent.IndexOf(':');
        if (separatorIndex <= 0 || separatorIndex == tagContent.Length - 1)
        {
            key = string.Empty;
            value = string.Empty;
            return false;
        }

        key = tagContent[..separatorIndex].Trim();
        value = tagContent[(separatorIndex + 1)..].Trim();
        return key.Length > 0;
    }

    private static bool ContainsRenderableText(string text) =>
        text.AsSpan().IndexOfAnyExcept(" \t".AsSpan()) >= 0;

    private static bool TryParseTimestamp(string rawValue, out double seconds)
    {
        seconds = 0;
        var value = rawValue.Trim();
        if (value.Length < 3)
        {
            return false;
        }

        var parts = value.Split(':', StringSplitOptions.TrimEntries);
        if (parts.Length is < 2 or > 3)
        {
            return false;
        }

        if (!int.TryParse(parts[^2], NumberStyles.None, CultureInfo.InvariantCulture, out var minutes))
        {
            return false;
        }

        if (!double.TryParse(
                parts[^1].Replace(',', '.'),
                NumberStyles.AllowDecimalPoint,
                CultureInfo.InvariantCulture,
                out var secondsPart))
        {
            return false;
        }

        var hours = 0;
        if (parts.Length == 3 &&
            !int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out hours))
        {
            return false;
        }

        if (parts.Length == 2 &&
            !int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out minutes))
        {
            return false;
        }

        if (secondsPart >= 60)
        {
            return false;
        }

        seconds = (hours * 3600d) + (minutes * 60d) + secondsPart;
        return true;
    }

    [GeneratedRegex(@"<(?<time>\d{1,3}:\d{1,2}(?::\d{1,2})?(?:[.,]\d{1,3})?)>", RegexOptions.CultureInvariant)]
    private static partial Regex InlineTimestamp();
}
