using Spectralis.App.Services;
using Spectralis.App.ViewModels;
using Spectralis.Core.Common;
using Spectralis.Core.Metadata;
using Xunit;

namespace Spectralis.Tests.App;

public sealed class RandomizerToolsViewModelTests : IDisposable
{
    private readonly string _wheelStore = Path.Combine(Path.GetTempPath(), $"wheels-{Guid.NewGuid():N}.json");
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"rand-lib-{Guid.NewGuid():N}.db");

    public RandomizerToolsViewModelTests() => SavedWheelStore.PathOverride = _wheelStore;

    public void Dispose()
    {
        SavedWheelStore.PathOverride = null;
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        foreach (var p in new[] { _wheelStore, _dbPath })
        {
            try { File.Delete(p); } catch { }
        }
    }

    private LibraryDatabase MakeLibrary(params string[] titles)
    {
        var db = new LibraryDatabase(_dbPath);
        var i = 0;
        foreach (var title in titles)
        {
            db.Upsert(new TrackInfo
            {
                SourcePath = $@"C:\music\{title}.mp3",
                Title = title,
                Artist = "Artist",
                Album = "Album",
                Duration = TimeSpan.FromMinutes(3),
                FormatName = "MP3",
            }, mtimeTicks: ++i);
        }
        return db;
    }

    [Fact]
    public void TextMode_NotepadStillDrivesEntries()
    {
        var vm = new RandomizerToolsViewModel();
        vm.NotepadText = "Red\nGreen\nBlue";

        Assert.Equal(new[] { "Red", "Green", "Blue" }, vm.WheelEntries.Select(e => e.Text));
        Assert.All(vm.WheelEntries, e => Assert.Equal(WheelEntryKind.Text, e.Kind));
    }

    [Fact]
    public void SwitchingToSongMode_DropsPlaceholderTextEntries()
    {
        var vm = new RandomizerToolsViewModel();
        Assert.NotEmpty(vm.WheelEntries); // three "Option N" defaults

        vm.SelectedMode = RandomizerMode.Songs;

        Assert.Empty(vm.WheelEntries);
        Assert.True(vm.IsSongMode);
        Assert.Equal("Pick a song", vm.SpinButtonText);
    }

    [Fact]
    public void AddCandidate_AddsSongEntry_AndDedupes()
    {
        var vm = new RandomizerToolsViewModel();
        vm.SelectedMode = RandomizerMode.Songs;

        var candidate = new MusicPickCandidate(WheelEntryKind.Song, @"C:\music\a.mp3", "A", "Artist");
        vm.AddCandidate(candidate);
        vm.AddCandidate(candidate);

        var entry = Assert.Single(vm.WheelEntries);
        Assert.Equal(WheelEntryKind.Song, entry.Kind);
        Assert.Equal(@"C:\music\a.mp3", entry.Ref);
        Assert.Equal("A", entry.Text);
        Assert.True(entry.IsMusic);
    }

    [Fact]
    public void FinishSpin_OnSong_EnablesPlayButton()
    {
        var vm = new RandomizerToolsViewModel();
        vm.SelectedMode = RandomizerMode.Songs;
        var winner = new WheelEntry("A", WheelEntryKind.Song, @"C:\music\a.mp3", "Artist");
        vm.WheelEntries.Add(winner);

        vm.FinishSpin(winner);

        Assert.Equal("A", vm.WheelResult);
        Assert.Same(winner, vm.WheelResultEntry);
        Assert.True(vm.CanPlayResult);
        Assert.Equal("Play song", vm.PlayResultText);
    }

    [Fact]
    public void FinishSpin_OnText_DoesNotOfferPlay()
    {
        var vm = new RandomizerToolsViewModel();
        var winner = vm.WheelEntries[0];

        vm.FinishSpin(winner);

        Assert.False(vm.CanPlayResult);
    }

    [Fact]
    public async Task PlayResult_OnSong_InvokesPlayHookWithRef()
    {
        IReadOnlyList<string>? played = null;
        var vm = new RandomizerToolsViewModel(
            playSongs: (paths, _) => { played = paths; return Task.CompletedTask; });
        vm.SelectedMode = RandomizerMode.Songs;
        var winner = new WheelEntry("A", WheelEntryKind.Song, @"C:\music\a.mp3", "Artist");
        vm.WheelEntries.Add(winner);
        vm.FinishSpin(winner);

        await vm.PlayResultAsync();

        Assert.Equal(new[] { @"C:\music\a.mp3" }, played);
    }

    [Fact]
    public void SaveAndLoadWheel_RoundTripsModeAndSongEntries()
    {
        var vm = new RandomizerToolsViewModel();
        vm.SelectedMode = RandomizerMode.Songs;
        vm.WheelEntries.Add(new WheelEntry("A", WheelEntryKind.Song, @"C:\music\a.mp3", "Artist"));
        vm.WheelEntries.Add(new WheelEntry("B", WheelEntryKind.Song, "spotify:track:xyz", "Band · Spotify"));
        vm.NewWheelName = "my mix";
        vm.SaveWheel();

        var fresh = new RandomizerToolsViewModel();
        var saved = Assert.Single(fresh.SavedWheels, w => w.Name == "my mix");
        fresh.LoadWheel(saved);

        Assert.Equal(RandomizerMode.Songs, fresh.SelectedMode);
        Assert.Equal(new[] { "A", "B" }, fresh.WheelEntries.Select(e => e.Text));
        Assert.Equal("spotify:track:xyz", fresh.WheelEntries[1].Ref);
        Assert.All(fresh.WheelEntries, e => Assert.Equal(WheelEntryKind.Song, e.Kind));
    }

    [Fact]
    public void AddCurrentQueue_PullsFromProvider()
    {
        var vm = new RandomizerToolsViewModel(
            currentQueueProvider: () => new[]
            {
                new MusicPickCandidate(WheelEntryKind.Song, @"C:\q\1.mp3", "One", ""),
                new MusicPickCandidate(WheelEntryKind.Song, @"C:\q\2.mp3", "Two", ""),
            });
        vm.SelectedMode = RandomizerMode.Songs;

        vm.AddCurrentQueue();

        Assert.Equal(new[] { "One", "Two" }, vm.WheelEntries.Select(e => e.Text));
    }

    [Fact]
    public void EnteringSongMode_LoadsLibraryAsCandidates()
    {
        using var db = MakeLibrary("Midnight Drive", "Sunrise", "Midnight City");
        var vm = new RandomizerToolsViewModel(library: db);

        // The SelectedMode setter rebuilds MusicResults synchronously for the Songs tab.
        vm.SelectedMode = RandomizerMode.Songs;

        Assert.Equal(3, vm.MusicResults.Count);
        Assert.All(vm.MusicResults, c => Assert.Equal(WheelEntryKind.Song, c.Kind));

        var pick = vm.MusicResults.First(c => c.Title == "Midnight Drive");
        vm.AddCandidate(pick);
        Assert.Contains(vm.WheelEntries, e => e.Ref == pick.Ref);
    }
}
