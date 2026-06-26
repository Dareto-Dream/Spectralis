using Spectralis.Core.Metadata;

namespace Spectralis.Core.Playlists;

public static class SmartPlaylistEvaluator
{
    public static List<string> Evaluate(SmartPlaylist playlist, IReadOnlyList<LibraryEntry> library)
    {
        IEnumerable<LibraryEntry> entries = library;

        if (playlist.Rules.Count > 0)
        {
            entries = playlist.Match == SmartMatchMode.All
                ? entries.Where(entry => playlist.Rules.All(rule => Matches(entry, rule)))
                : entries.Where(entry => playlist.Rules.Any(rule => Matches(entry, rule)));
        }

        entries = Sort(entries, playlist.SortBy, playlist.SortDescending);

        if (playlist.Limit > 0)
        {
            entries = entries.Take(playlist.Limit);
        }

        return entries.Select(entry => entry.Track.SourcePath).ToList();
    }

    private static bool Matches(LibraryEntry entry, SmartRule rule)
    {
        var track = entry.Track;
        var value = rule.Value;

        return rule.Field switch
        {
            SmartRuleField.Title => CompareText(track.Title, rule.Op, value),
            SmartRuleField.Artist => CompareText(track.Artist, rule.Op, value),
            SmartRuleField.Album => CompareText(track.Album, rule.Op, value),
            SmartRuleField.AlbumArtist => CompareText(track.AlbumArtist, rule.Op, value),
            SmartRuleField.Genre => CompareText(track.Genre, rule.Op, value),
            SmartRuleField.Year => int.TryParse(value, out var year) && CompareNum((int)track.Year, rule.Op, year),
            SmartRuleField.PlayCount => int.TryParse(value, out var plays) && CompareNum(entry.PlayCount, rule.Op, plays),
            SmartRuleField.Duration => double.TryParse(value, out var duration) && CompareNum((int)track.Duration.TotalSeconds, rule.Op, (int)duration),
            _ => false,
        };
    }

    private static bool CompareText(string field, SmartRuleOp op, string value) =>
        op switch
        {
            SmartRuleOp.Contains => field.Contains(value, StringComparison.OrdinalIgnoreCase),
            SmartRuleOp.NotContains => !field.Contains(value, StringComparison.OrdinalIgnoreCase),
            SmartRuleOp.Is => string.Equals(field, value, StringComparison.OrdinalIgnoreCase),
            SmartRuleOp.IsNot => !string.Equals(field, value, StringComparison.OrdinalIgnoreCase),
            _ => false,
        };

    private static bool CompareNum(int field, SmartRuleOp op, int value) =>
        op switch
        {
            SmartRuleOp.Is => field == value,
            SmartRuleOp.IsNot => field != value,
            SmartRuleOp.GreaterThan => field > value,
            SmartRuleOp.LessThan => field < value,
            _ => false,
        };

    private static IOrderedEnumerable<LibraryEntry> Sort(
        IEnumerable<LibraryEntry> entries, SmartSortField field, bool descending)
    {
        Func<LibraryEntry, object> key = field switch
        {
            SmartSortField.Artist => entry => entry.Track.Artist,
            SmartSortField.Album => entry => entry.Track.Album,
            SmartSortField.Year => entry => (object)entry.Track.Year,
            SmartSortField.PlayCount => entry => (object)entry.PlayCount,
            SmartSortField.Duration => entry => (object)entry.Track.Duration.TotalSeconds,
            SmartSortField.DateAdded => entry => (object)entry.DateAdded,
            SmartSortField.LastPlayed => entry => (object)(entry.LastPlayed ?? DateTime.MinValue),
            _ => entry => entry.Track.Title,
        };

        return descending
            ? entries.OrderByDescending(key)
            : entries.OrderBy(key);
    }
}
