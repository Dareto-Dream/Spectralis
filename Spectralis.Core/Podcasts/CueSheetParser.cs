using System.Globalization;
using System.Text.RegularExpressions;

namespace Spectralis.Core.Podcasts;

/// <summary>
/// Parses a CUE sheet into chapters — one chapter per <c>TRACK</c>, starting at its
/// <c>INDEX 01</c> (MM:SS:FF, where FF is a frame at 75/second), titled by <c>TITLE</c>.
/// </summary>
public static partial class CueSheetParser
{
    public static IReadOnlyList<Chapter> Parse(string cueText)
    {
        var chapters = new List<(TimeSpan Start, string Title)>();
        string? pendingTitle = null;
        var inTrack = false;

        foreach (var raw in cueText.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0)
            {
                continue;
            }

            if (line.StartsWith("TRACK ", StringComparison.OrdinalIgnoreCase))
            {
                inTrack = true;
                pendingTitle = null;
                continue;
            }

            if (inTrack && line.StartsWith("TITLE ", StringComparison.OrdinalIgnoreCase))
            {
                pendingTitle = Unquote(line[6..].Trim());
                continue;
            }

            if (inTrack && line.StartsWith("INDEX 01 ", StringComparison.OrdinalIgnoreCase))
            {
                var match = IndexTimeRegex().Match(line);
                if (match.Success)
                {
                    var minutes = int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
                    var seconds = int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture);
                    var frames = int.Parse(match.Groups[3].Value, CultureInfo.InvariantCulture);
                    var start = TimeSpan.FromSeconds((minutes * 60) + seconds + (frames / 75.0));
                    chapters.Add((start, pendingTitle ?? $"Chapter {chapters.Count + 1}"));
                }

                inTrack = false;
            }
        }

        if (chapters.Count == 0)
        {
            return Array.Empty<Chapter>();
        }

        chapters.Sort((a, b) => a.Start.CompareTo(b.Start));
        var result = new List<Chapter>(chapters.Count);
        for (var i = 0; i < chapters.Count; i++)
        {
            TimeSpan? end = i + 1 < chapters.Count ? chapters[i + 1].Start : null;
            result.Add(new Chapter(chapters[i].Start, end, chapters[i].Title));
        }

        return result;
    }

    private static string Unquote(string value) =>
        value.Length >= 2 && value[0] == '"' && value[^1] == '"' ? value[1..^1] : value;

    [GeneratedRegex(@"INDEX\s+01\s+(\d+):(\d{2}):(\d{2})", RegexOptions.IgnoreCase)]
    private static partial Regex IndexTimeRegex();
}
