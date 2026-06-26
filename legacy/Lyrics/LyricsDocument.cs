using System.Collections.ObjectModel;

namespace Spectralis;

public sealed class LyricsDocument
{
    private readonly LyricsLine[] lines;
    private readonly ReadOnlyDictionary<string, string> metadata;

    public LyricsDocument(
        IEnumerable<LyricsLine> lines,
        IReadOnlyDictionary<string, string>? metadata = null,
        int offsetMilliseconds = 0,
        string sourceLabel = "Synced lyrics",
        bool isDescription = false)
    {
        this.lines = lines
            .OrderBy(static line => line.StartTime)
            .ToArray();

        this.metadata = new ReadOnlyDictionary<string, string>(
            metadata is null
                ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, string>(metadata, StringComparer.OrdinalIgnoreCase));

        OffsetMilliseconds = offsetMilliseconds;
        SourceLabel = sourceLabel;
        IsDescription = isDescription;
    }

    public IReadOnlyList<LyricsLine> Lines => lines;

    public IReadOnlyDictionary<string, string> Metadata => metadata;

    public int OffsetMilliseconds { get; }

    public string SourceLabel { get; }

    public bool IsDescription { get; }

    public bool HasWordTimings => lines.Any(static line => line.Segments.Count > 1);

    public bool HasLines => lines.Length > 0;

    public string? Title => GetMetadata("ti");

    public string? Artist => GetMetadata("ar");

    public string? Album => GetMetadata("al");

    public int FindLineIndex(double positionSeconds)
    {
        if (lines.Length == 0)
        {
            return -1;
        }

        var target = Math.Max(0, positionSeconds);
        var low = 0;
        var high = lines.Length - 1;
        var result = -1;

        while (low <= high)
        {
            var mid = low + ((high - low) / 2);
            if (lines[mid].StartTime <= target + 0.0005d)
            {
                result = mid;
                low = mid + 1;
            }
            else
            {
                high = mid - 1;
            }
        }

        return result;
    }

    public string? GetMetadata(string key) =>
        metadata.TryGetValue(key, out var value) ? value : null;
}

public sealed class LyricsLine
{
    private readonly LyricsSegment[] segments;

    public LyricsLine(double startTime, string text, IEnumerable<LyricsSegment>? segments = null, string? explanation = null)
    {
        StartTime = Math.Max(0, startTime);
        Text = text ?? string.Empty;
        Explanation = explanation;
        this.segments = (segments ?? [])
            .OrderBy(static segment => segment.StartTime)
            .ToArray();
    }

    public double StartTime { get; }

    public string Text { get; }

    public string? Explanation { get; }

    public IReadOnlyList<LyricsSegment> Segments => segments;

    public int FindActiveSegmentIndex(double positionSeconds)
    {
        if (segments.Length == 0)
        {
            return -1;
        }

        var target = Math.Max(0, positionSeconds);
        var low = 0;
        var high = segments.Length - 1;
        var result = -1;

        while (low <= high)
        {
            var mid = low + ((high - low) / 2);
            if (segments[mid].StartTime <= target + 0.0005d)
            {
                result = mid;
                low = mid + 1;
            }
            else
            {
                high = mid - 1;
            }
        }

        return result;
    }

    public LyricsLine ShiftedBy(double deltaSeconds) =>
        new(
            StartTime + deltaSeconds,
            Text,
            segments.Select(segment => segment.ShiftedBy(deltaSeconds)),
            Explanation);
}

public readonly record struct LyricsSegment(double StartTime, string Text)
{
    public LyricsSegment ShiftedBy(double deltaSeconds) =>
        new(Math.Max(0, StartTime + deltaSeconds), Text);
}
