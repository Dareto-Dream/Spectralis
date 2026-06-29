namespace Spectralis.Core.Scrobbling;

public sealed class ArtistStat
{
    public string Artist { get; init; } = "";
    public int Plays { get; set; }
}

public sealed class TrackStat
{
    public string Title { get; init; } = "";
    public string Artist { get; init; } = "";
    public int Plays { get; set; }
}

public sealed class ListeningStats
{
    public int TotalScrobbles { get; init; }
    public double TotalHours { get; init; }
    public int CurrentStreakDays { get; init; }
    public int LongestStreakDays { get; init; }

    public List<ArtistStat> TopArtists { get; init; } = [];
    public List<TrackStat> TopTracks { get; init; } = [];

    public static ListeningStats Compute(IList<ScrobbleRecord> history, DateTime since)
    {
        var sinceTs = new DateTimeOffset(since, TimeSpan.Zero).ToUnixTimeSeconds();
        var filtered = history.Where(r => r.Timestamp >= sinceTs).ToList();

        var total = filtered.Count;
        var hours = filtered.Sum(r => r.Duration) / 3600.0;

        var artists = filtered
            .Where(r => !string.IsNullOrWhiteSpace(r.Artist))
            .GroupBy(r => r.Artist, StringComparer.OrdinalIgnoreCase)
            .Select(g => new ArtistStat { Artist = g.Key, Plays = g.Count() })
            .OrderByDescending(a => a.Plays)
            .Take(25)
            .ToList();

        var tracks = filtered
            .Where(r => !string.IsNullOrWhiteSpace(r.Title))
            .GroupBy(r => (Title: r.Title.ToUpperInvariant(), Artist: r.Artist.ToUpperInvariant()))
            .Select(g => new TrackStat { Title = g.First().Title, Artist = g.First().Artist, Plays = g.Count() })
            .OrderByDescending(t => t.Plays)
            .Take(25)
            .ToList();

        var (current, longest) = ComputeStreaks(history);

        return new ListeningStats
        {
            TotalScrobbles = total,
            TotalHours = hours,
            CurrentStreakDays = current,
            LongestStreakDays = longest,
            TopArtists = artists,
            TopTracks = tracks,
        };
    }

    private static (int Current, int Longest) ComputeStreaks(IList<ScrobbleRecord> history)
    {
        if (history.Count == 0)
        {
            return (0, 0);
        }

        var activeDays = history
            .Select(r => DateTimeOffset.FromUnixTimeSeconds(r.Timestamp).UtcDateTime.Date)
            .Distinct()
            .OrderDescending()
            .ToList();

        if (activeDays.Count == 0)
        {
            return (0, 0);
        }

        var today = DateTime.UtcNow.Date;
        var current = 0;
        if (activeDays[0] == today || activeDays[0] == today.AddDays(-1))
        {
            var expected = activeDays[0];
            foreach (var day in activeDays)
            {
                if (day == expected)
                {
                    current++;
                    expected = expected.AddDays(-1);
                }
                else
                {
                    break;
                }
            }
        }

        var longest = 0;
        var run = 1;
        for (var i = 1; i < activeDays.Count; i++)
        {
            if ((activeDays[i - 1] - activeDays[i]).Days == 1)
            {
                run++;
                if (run > longest)
                {
                    longest = run;
                }
            }
            else
            {
                if (run > longest)
                {
                    longest = run;
                }

                run = 1;
            }
        }

        if (run > longest)
        {
            longest = run;
        }

        return (current, Math.Max(longest, current));
    }
}

public sealed record ListeningActivitySnapshot(
    int TotalScrobbles,
    double TotalHours,
    int CurrentStreakDays,
    string TopArtist,
    int TopArtistPlays,
    string TopTrackTitle,
    string TopTrackArtist,
    int TopTrackPlays)
{
    public static ListeningActivitySnapshot Empty { get; } = new(0, 0, 0, "", 0, "", "", 0);

    public bool HasHistory => TotalScrobbles > 0;

    public string TopTrackDisplay =>
        string.IsNullOrWhiteSpace(TopTrackTitle)
            ? ""
            : string.IsNullOrWhiteSpace(TopTrackArtist)
                ? TopTrackTitle
                : $"{TopTrackArtist} - {TopTrackTitle}";

    public static ListeningActivitySnapshot FromHistory(IList<ScrobbleRecord> history)
    {
        if (history.Count == 0)
        {
            return Empty;
        }

        var stats = ListeningStats.Compute(history, DateTime.MinValue);
        var topArtist = stats.TopArtists.FirstOrDefault();
        var topTrack = stats.TopTracks.FirstOrDefault();
        return new ListeningActivitySnapshot(
            stats.TotalScrobbles,
            stats.TotalHours,
            stats.CurrentStreakDays,
            topArtist?.Artist ?? "",
            topArtist?.Plays ?? 0,
            topTrack?.Title ?? "",
            topTrack?.Artist ?? "",
            topTrack?.Plays ?? 0);
    }
}
