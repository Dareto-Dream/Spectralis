using System.Globalization;

namespace Spectralis.Core.Scrobbling;

public sealed record WrappedEntry(int Rank, string Name, int Plays);

/// <summary>
/// The handful of numbers a shareable "wrapped" card shows. Kept separate from drawing so the
/// content rules (top 5, "no data" handling, wording) are testable without a renderer.
/// </summary>
public sealed record WrappedSummary(
    string PeriodLabel,
    int Scrobbles,
    string HoursText,
    int LongestStreakDays,
    IReadOnlyList<WrappedEntry> TopArtists,
    IReadOnlyList<WrappedEntry> TopTracks)
{
    public const int ListSize = 5;

    public bool HasData => Scrobbles > 0;

    public string? TopArtistName => TopArtists.Count > 0 ? TopArtists[0].Name : null;

    public static WrappedSummary From(ListeningStats stats, string periodLabel) => new(
        periodLabel,
        stats.TotalScrobbles,
        stats.TotalHours.ToString(stats.TotalHours >= 100 ? "0" : "0.#", CultureInfo.InvariantCulture),
        stats.LongestStreakDays,
        stats.TopArtists.Take(ListSize)
            .Select((a, i) => new WrappedEntry(i + 1, a.Artist, a.Plays)).ToList(),
        stats.TopTracks.Take(ListSize)
            .Select((t, i) => new WrappedEntry(i + 1,
                string.IsNullOrWhiteSpace(t.Artist) ? t.Title : $"{t.Artist} - {t.Title}", t.Plays)).ToList());
}
