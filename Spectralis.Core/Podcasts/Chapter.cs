namespace Spectralis.Core.Podcasts;

/// <summary>One chapter / segment marker within a podcast episode or audiobook.</summary>
public sealed record Chapter(TimeSpan Start, TimeSpan? End, string Title, string? Url = null);

/// <summary>
/// An ordered set of chapters for a single track, with the source it was read from
/// ("Embedded" / "chapters.json" / "CUE"). Chapters are sorted by <see cref="Chapter.Start"/>.
/// </summary>
public sealed class ChapterList
{
    public static readonly ChapterList Empty = new(Array.Empty<Chapter>(), string.Empty);

    private readonly double[] _startsSeconds;

    public ChapterList(IReadOnlyList<Chapter> chapters, string sourceLabel)
    {
        Chapters = chapters;
        SourceLabel = sourceLabel;
        _startsSeconds = chapters.Select(c => c.Start.TotalSeconds).ToArray();
    }

    public IReadOnlyList<Chapter> Chapters { get; }

    public string SourceLabel { get; }

    public bool IsEmpty => Chapters.Count == 0;

    public int Count => Chapters.Count;

    /// <summary>Chapter start times in seconds — for the scrubber tick overlay.</summary>
    public IReadOnlyList<double> StartSeconds => _startsSeconds;

    /// <summary>
    /// Index of the chapter active at <paramref name="positionSeconds"/> (the last one whose
    /// start is at or before that position), or -1 before the first chapter. Mirrors
    /// <c>LyricsDocument.FindLineIndex</c>.
    /// </summary>
    public int FindActiveIndex(double positionSeconds)
    {
        if (_startsSeconds.Length == 0)
        {
            return -1;
        }

        var target = Math.Max(0, positionSeconds);
        var low = 0;
        var high = _startsSeconds.Length - 1;
        var result = -1;

        while (low <= high)
        {
            var mid = low + ((high - low) / 2);
            if (_startsSeconds[mid] <= target + 0.0005d)
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
}
