using Spectralis.Core.Scrobbling;
using Xunit;

namespace Spectralis.Tests.Core;

public class WrappedSummaryTests
{
    private static ScrobbleRecord Play(string artist, string title, long ts = 1_700_000_000) =>
        new() { Artist = artist, Title = title, Timestamp = ts, Duration = 180 };

    [Fact]
    public void Empty_history_has_no_data()
    {
        var summary = WrappedSummary.From(ListeningStats.Compute([], DateTime.MinValue), "All Time");

        Assert.False(summary.HasData);
        Assert.Null(summary.TopArtistName);
        Assert.Empty(summary.TopTracks);
    }

    [Fact]
    public void Lists_are_ranked_and_capped_at_five()
    {
        var history = new List<ScrobbleRecord>();
        for (var i = 0; i < 8; i++)
            for (var n = 0; n <= i; n++)
                history.Add(Play($"Artist {i}", $"Song {i}"));

        var summary = WrappedSummary.From(ListeningStats.Compute(history, DateTime.MinValue), "This Month");

        Assert.Equal(5, summary.TopArtists.Count);
        Assert.Equal("Artist 7", summary.TopArtistName);
        Assert.Equal(8, summary.TopArtists[0].Plays);
        Assert.Equal([1, 2, 3, 4, 5], summary.TopArtists.Select(a => a.Rank));
        Assert.Equal("This Month", summary.PeriodLabel);
    }

    [Fact]
    public void Tracks_show_artist_dash_title_and_hours_are_formatted()
    {
        var summary = WrappedSummary.From(
            ListeningStats.Compute([Play("ARTIST", "SONG"), Play("ARTIST", "SONG")], DateTime.MinValue), "All Time");

        Assert.Equal("ARTIST - SONG", summary.TopTracks[0].Name);
        Assert.Equal("0.1", summary.HoursText);
        Assert.Equal(2, summary.Scrobbles);
    }
}
