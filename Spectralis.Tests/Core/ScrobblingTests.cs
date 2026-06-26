using Spectralis.Core.Scrobbling;
using Xunit;

namespace Spectralis.Tests.Core;

public sealed class ScrobblingTests : IDisposable
{
    private readonly string _tempDir;

    public ScrobblingTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"spectralis-scrob-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        ScrobbleQueue.QueuePath = Path.Combine(_tempDir, "queue.json");
        ScrobbleQueue.HistoryPath = Path.Combine(_tempDir, "history.json");
    }

    public void Dispose()
    {
        ScrobbleQueue.QueuePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Spectralis", "scrobble-queue.json");
        ScrobbleQueue.HistoryPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Spectralis", "scrobble-history.json");
        try
        {
            Directory.Delete(_tempDir, recursive: true);
        }
        catch
        {
        }
    }

    private static ScrobbleRecord Record(string title, string artist, DateTime at, double duration = 200) =>
        new()
        {
            Title = title,
            Artist = artist,
            Timestamp = new DateTimeOffset(at, TimeSpan.Zero).ToUnixTimeSeconds(),
            Duration = duration,
        };

    [Fact]
    public void Queue_EnqueueDrainRestore_Persists()
    {
        var queue = new ScrobbleQueue();
        queue.Enqueue(Record("One", "A", DateTime.UtcNow));
        queue.Enqueue(Record("Two", "B", DateTime.UtcNow));

        var reloaded = new ScrobbleQueue();
        reloaded.Load();
        Assert.Equal(2, reloaded.Count);

        var drained = reloaded.Drain();
        Assert.Equal(2, drained.Count);
        Assert.Equal(0, reloaded.Count);

        reloaded.RestoreAll(drained);
        Assert.Equal(2, reloaded.Count);
    }

    [Fact]
    public void Service_ScrobblesAfterHalfDuration()
    {
        var config = new ScrobblingConfig(false, "", "", "", false, "");
        var service = new ScrobblingService(() => config);

        service.NotifyTrackLoaded("x.mp3", "Song", "Artist", "Album", durationSeconds: 100);

        // 100s track scrobbles at 50s listened: ten 5-second ticks.
        for (var i = 0; i < 9; i++)
        {
            service.Tick(i * 5, isPlaying: true);
        }

        Assert.Empty(ScrobbleQueue.LoadHistory());

        service.Tick(50, isPlaying: true);

        var history = ScrobbleQueue.LoadHistory();
        var record = Assert.Single(history);
        Assert.Equal("Song", record.Title);
        Assert.Equal("Artist", record.Artist);
    }

    [Fact]
    public void Service_SkipsShortTracksAndPaused()
    {
        var config = new ScrobblingConfig(false, "", "", "", false, "");
        var service = new ScrobblingService(() => config);

        // Under 30 seconds: never scrobbles regardless of listened time.
        service.NotifyTrackLoaded("short.mp3", "Short", "Artist", "", durationSeconds: 20);
        for (var i = 0; i < 20; i++)
        {
            service.Tick(i, isPlaying: true);
        }

        Assert.Empty(ScrobbleQueue.LoadHistory());

        // Paused ticks do not accumulate listened time.
        service.NotifyTrackLoaded("long.mp3", "Long", "Artist", "", durationSeconds: 100);
        for (var i = 0; i < 40; i++)
        {
            service.Tick(i, isPlaying: false);
        }

        Assert.Empty(ScrobbleQueue.LoadHistory());
    }

    [Fact]
    public void Stats_ComputeTotalsTopsAndStreaks()
    {
        var today = DateTime.UtcNow.Date.AddHours(12);
        var history = new List<ScrobbleRecord>
        {
            Record("A", "X", today, 3600),
            Record("A", "X", today.AddDays(-1), 1800),
            Record("B", "Y", today.AddDays(-1), 1800),
            Record("C", "X", today.AddDays(-5), 600),
        };

        var stats = ListeningStats.Compute(history, DateTime.MinValue);

        Assert.Equal(4, stats.TotalScrobbles);
        Assert.Equal(2.166, stats.TotalHours, 2);
        Assert.Equal(2, stats.CurrentStreakDays);
        Assert.Equal("X", stats.TopArtists[0].Artist);
        Assert.Equal(3, stats.TopArtists[0].Plays);
        Assert.Equal("A", stats.TopTracks[0].Title);

        var weekStats = ListeningStats.Compute(history, DateTime.UtcNow.AddDays(-2));
        Assert.Equal(3, weekStats.TotalScrobbles);
    }

    [Fact]
    public void ActivitySnapshot_FromHistory_PullsFavoriteTrackAndArtist()
    {
        var today = DateTime.UtcNow.Date.AddHours(12);
        var history = new List<ScrobbleRecord>
        {
            Record("Loop", "Delta", today, 180),
            Record("Loop", "Delta", today.AddDays(-1), 180),
            Record("Side", "Delta", today.AddDays(-2), 120),
            Record("Other", "Someone", today.AddDays(-3), 200),
        };

        var snapshot = ListeningActivitySnapshot.FromHistory(history);

        Assert.True(snapshot.HasHistory);
        Assert.Equal(4, snapshot.TotalScrobbles);
        Assert.Equal("Delta", snapshot.TopArtist);
        Assert.Equal(3, snapshot.TopArtistPlays);
        Assert.Equal("Loop", snapshot.TopTrackTitle);
        Assert.Equal("Delta - Loop", snapshot.TopTrackDisplay);
        Assert.Equal(2, snapshot.TopTrackPlays);
    }

    [Fact]
    public void ActivitySnapshot_FromHistory_HandlesEmptyHistory()
    {
        var snapshot = ListeningActivitySnapshot.FromHistory([]);

        Assert.False(snapshot.HasHistory);
        Assert.Equal(0, snapshot.TotalScrobbles);
        Assert.Equal("", snapshot.TopTrackDisplay);
    }
}
