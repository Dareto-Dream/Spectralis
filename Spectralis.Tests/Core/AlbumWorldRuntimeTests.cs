using System.Text.Json;
using Spectralis.Core.Capsule;
using Xunit;

namespace Spectralis.Tests.Core;

public sealed class AlbumWorldRuntimeTests
{
    [Fact]
    public void ReadyState_IncludesRestoredSessionFields()
    {
        var runtime = new AlbumWorldRuntime();
        var session = new AlbumWorldSession
        {
            CurrentTrackId = "t2",
            CurrentPositionSeconds = 42.5,
            IntroCompleted = true,
            UnlockedAchievements = ["ach-one"],
            LevelGateProgress = 2,
            TrackStats =
            {
                ["t1"] = new AlbumTrackStats { PlayCount = 1, PlayedSeconds = 12, Completed = true },
            },
        };

        runtime.Load(BuildManifest(), Path.GetTempPath(), session);

        using var doc = JsonDocument.Parse(runtime.BuildWorldStateJson());
        var state = doc.RootElement;
        var restored = state.GetProperty("session");

        Assert.Equal("t2", restored.GetProperty("currentTrackId").GetString());
        Assert.Equal(42.5, restored.GetProperty("currentPositionSeconds").GetDouble());
        Assert.True(restored.GetProperty("introCompleted").GetBoolean());
        Assert.Equal(2, restored.GetProperty("levelGateProgress").GetInt32());
        Assert.Equal("ach-one", restored.GetProperty("unlockedAchievements")[0].GetString());
        Assert.True(restored.GetProperty("trackStats").GetProperty("t1").GetProperty("completed").GetBoolean());
    }

    [Fact]
    public void TrackLifecycle_RecordsStartPositionAndCompletion()
    {
        var runtime = new AlbumWorldRuntime();
        var session = new AlbumWorldSession();

        runtime.Load(BuildManifest(), Path.GetTempPath(), session);
        runtime.NotifyTrackStarted("t1", 7.25);
        runtime.Tick(8.5, engineIsPlaying: true);
        runtime.NotifyTrackCompleted("t1");

        Assert.Equal("t1", session.CurrentTrackId);
        Assert.Equal(0, session.CurrentPositionSeconds);
        Assert.True(session.TrackStats["t1"].Completed);
        Assert.Equal(1, session.TrackStats["t1"].PlayCount);
    }

    private static AlbumManifest BuildManifest() => new()
    {
        Id = "album-1",
        Title = "Album",
        Artist = "Artist",
        Tracks =
        [
            new AlbumTrackEntry
            {
                Id = "t1",
                Title = "Track One",
                Artist = "Artist",
                Audio = new CapsuleAudio { Entry = "t1.mp3", DurationSeconds = 10 },
            },
            new AlbumTrackEntry
            {
                Id = "t2",
                Title = "Track Two",
                Artist = "Artist",
                Audio = new CapsuleAudio { Entry = "t2.mp3", DurationSeconds = 20 },
            },
        ],
    };
}
