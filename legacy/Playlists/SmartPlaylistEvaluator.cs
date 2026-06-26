namespace Spectralis;

internal static class SmartPlaylistEvaluator
{
    public static List<string> Evaluate(SmartPlaylist pl, MusicLibrary library)
    {
        IEnumerable<LibraryTrack> tracks = library.Tracks;

        if (pl.Rules.Count > 0)
        {
            tracks = pl.Match == SmartMatchMode.All
                ? tracks.Where(t => pl.Rules.All(r => Matches(t, r)))
                : tracks.Where(t => pl.Rules.Any(r => Matches(t, r)));
        }

        tracks = Sort(tracks, pl.SortBy, pl.SortDescending);

        if (pl.Limit > 0)
            tracks = tracks.Take(pl.Limit);

        return tracks.Select(t => t.Path).ToList();
    }

    private static bool Matches(LibraryTrack t, SmartRule rule)
    {
        var value = rule.Value;

        return rule.Field switch
        {
            SmartRuleField.Title       => CompareText(t.Title,       rule.Op, value),
            SmartRuleField.Artist      => CompareText(t.Artist,      rule.Op, value),
            SmartRuleField.Album       => CompareText(t.Album,       rule.Op, value),
            SmartRuleField.AlbumArtist => CompareText(t.AlbumArtist, rule.Op, value),
            SmartRuleField.Genre       => CompareText(t.Genre,       rule.Op, value),
            SmartRuleField.Year        => int.TryParse(value, out var y) && CompareNum(t.Year,      rule.Op, y),
            SmartRuleField.PlayCount   => int.TryParse(value, out var p) && CompareNum(t.PlayCount, rule.Op, p),
            SmartRuleField.Duration    => double.TryParse(value, out var d) && CompareNum((int)t.DurationSeconds, rule.Op, (int)d),
            _                          => false,
        };
    }

    private static bool CompareText(string field, SmartRuleOp op, string value) =>
        op switch
        {
            SmartRuleOp.Contains    => field.Contains(value, StringComparison.OrdinalIgnoreCase),
            SmartRuleOp.NotContains => !field.Contains(value, StringComparison.OrdinalIgnoreCase),
            SmartRuleOp.Is          => string.Equals(field, value, StringComparison.OrdinalIgnoreCase),
            SmartRuleOp.IsNot       => !string.Equals(field, value, StringComparison.OrdinalIgnoreCase),
            _                       => false,
        };

    private static bool CompareNum(int field, SmartRuleOp op, int value) =>
        op switch
        {
            SmartRuleOp.Is          => field == value,
            SmartRuleOp.IsNot       => field != value,
            SmartRuleOp.GreaterThan => field > value,
            SmartRuleOp.LessThan    => field < value,
            _                       => false,
        };

    private static IOrderedEnumerable<LibraryTrack> Sort(
        IEnumerable<LibraryTrack> tracks, SmartSortField field, bool descending)
    {
        Func<LibraryTrack, object> key = field switch
        {
            SmartSortField.Artist    => t => t.Artist,
            SmartSortField.Album     => t => t.Album,
            SmartSortField.Year      => t => (object)t.Year,
            SmartSortField.PlayCount => t => (object)t.PlayCount,
            SmartSortField.Duration  => t => (object)t.DurationSeconds,
            SmartSortField.DateAdded => t => (object)t.DateAdded,
            SmartSortField.LastPlayed => t => (object)(t.LastPlayed ?? DateTime.MinValue),
            _                        => t => t.Title,
        };

        return descending
            ? tracks.OrderByDescending(key)
            : tracks.OrderBy(key);
    }
}
