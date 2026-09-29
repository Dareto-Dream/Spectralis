using System.Text.Json;
using Spectralis.Core.Capsule;
using Xunit;

namespace Spectralis.Tests.Core;

public sealed class AlbumWorldRuntimeTests
{
    [Fact]
    public void WorldHtmlContext_CarriesSourceDirectoryForHostedNavigation()
    {
        var albumDir = Path.Combine(Path.GetTempPath(), $"spectralis-album-world-{Guid.NewGuid():N}");
        Directory.CreateDirectory(albumDir);
        try
        {
            File.WriteAllText(Path.Combine(albumDir, "world.html"), "<html><body>world</body></html>");
            var manifest = BuildManifest();
            manifest.World = new AlbumWorldSection { Entry = "world.html" };
            var runtime = new AlbumWorldRuntime();
            runtime.Load(manifest, albumDir, new AlbumWorldSession());

            var context = runtime.BuildWorldHtmlContext();

            Assert.NotNull(context);
            Assert.Equal(albumDir, context!.SourceDirectory);
        }
        finally
        {
            try { Directory.Delete(albumDir, recursive: true); } catch { }
        }
    }

    [Fact]
    public void WorldHtmlContext_CarriesManifestCapabilities()
    {
        // Without this, every capability-gated feature (worlds.wasm3d, audio.dspPreset,
        // presence.richPresence, worlds.pointerLock) reads as denied for every album world
        // regardless of what its manifest actually declares.
        var albumDir = Path.Combine(Path.GetTempPath(), $"spectralis-album-world-{Guid.NewGuid():N}");
        Directory.CreateDirectory(albumDir);
        try
        {
            File.WriteAllText(Path.Combine(albumDir, "world.html"), "<html><body>world</body></html>");
            var manifest = BuildManifest();
            manifest.World = new AlbumWorldSection { Entry = "world.html" };
            manifest.Capabilities = ["worlds.wasm3d", "worlds.pointerLock"];
            var runtime = new AlbumWorldRuntime();
            runtime.Load(manifest, albumDir, new AlbumWorldSession());

            var context = runtime.BuildWorldHtmlContext();

            Assert.NotNull(context);
            Assert.Contains("worlds.wasm3d", context!.Capabilities);
            Assert.Contains("worlds.pointerLock", context.Capabilities);
        }
        finally
        {
            try { Directory.Delete(albumDir, recursive: true); } catch { }
        }
    }

    [Fact]
    public void WorldHtmlContext_CarriesCustomPauseMenu_OnlyWhenCapabilityDeclared()
    {
        // worlds.pauseMenu gates whether the manifest's own pause menu copy is honored — a
        // manifest with the pauseMenu block but not the capability should be ignored in favor
        // of the app's built-in default, same "declared but not granted" shape as every other
        // capability gate here.
        var albumDir = Path.Combine(Path.GetTempPath(), $"spectralis-album-world-{Guid.NewGuid():N}");
        Directory.CreateDirectory(albumDir);
        try
        {
            File.WriteAllText(Path.Combine(albumDir, "world.html"), "<html><body>world</body></html>");
            var pauseMenu = new AlbumPauseMenuConfig { Title = "HOLD UP", ResumeLabel = "Back In" };

            var withoutCapability = BuildManifest();
            withoutCapability.World = new AlbumWorldSection { Entry = "world.html", PauseMenu = pauseMenu };
            withoutCapability.Capabilities = ["worlds.pointerLock"];
            var runtimeWithout = new AlbumWorldRuntime();
            runtimeWithout.Load(withoutCapability, albumDir, new AlbumWorldSession());
            Assert.Null(runtimeWithout.BuildWorldHtmlContext()!.PauseMenu);

            var withCapability = BuildManifest();
            withCapability.World = new AlbumWorldSection { Entry = "world.html", PauseMenu = pauseMenu };
            withCapability.Capabilities = ["worlds.pointerLock", "worlds.pauseMenu"];
            var runtimeWith = new AlbumWorldRuntime();
            runtimeWith.Load(withCapability, albumDir, new AlbumWorldSession());
            var resolved = runtimeWith.BuildWorldHtmlContext()!.PauseMenu;

            Assert.NotNull(resolved);
            Assert.Equal("HOLD UP", resolved!.Title);
            Assert.Equal("Back In", resolved.ResumeLabel);
        }
        finally
        {
            try { Directory.Delete(albumDir, recursive: true); } catch { }
        }
    }

    [Fact]
    public void AchievementsSnapshot_DerivesPlayedAndHeardThemAllFromTrackStats()
    {
        var manifest = BuildManifest(); // t1, t2
        var session = new AlbumWorldSession
        {
            TrackStats =
            {
                ["t1"] = new AlbumTrackStats { PlayCount = 1 },
            },
            UnlockedAchievements = ["walked-into-chaser"],
        };
        var runtime = new AlbumWorldRuntime();
        runtime.Load(manifest, Path.GetTempPath(), session);

        var entries = runtime.BuildAchievementsSnapshot();

        var playedT1 = Assert.Single(entries, e => e.Id == "played-t1");
        Assert.True(playedT1.Unlocked);
        Assert.False(playedT1.Hidden);

        var playedT2 = Assert.Single(entries, e => e.Id == "played-t2");
        Assert.False(playedT2.Unlocked);

        // Persisted hidden achievement, present in UnlockedAchievements.
        Assert.True(Assert.Single(entries, e => e.Id == "walked-into-chaser").Unlocked);
        // Persisted hidden achievement, absent.
        Assert.False(Assert.Single(entries, e => e.Id == "found-the-mirror").Unlocked);
        // Derived hidden achievement: only t1 has been played, not t2, so not unlocked yet.
        Assert.False(Assert.Single(entries, e => e.Id == "heard-them-all").Unlocked);

        // Now play t2 too — the derived achievement should flip on without any new bookkeeping.
        session.TrackStats["t2"] = new AlbumTrackStats { PlayCount = 1 };
        entries = runtime.BuildAchievementsSnapshot();
        Assert.True(Assert.Single(entries, e => e.Id == "heard-them-all").Unlocked);
    }

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
