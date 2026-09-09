using Spectralis.App.Services;
using Spectralis.App.ViewModels;
using Spectralis.Core.Common;
using Spectralis.Core.Metadata;
using Xunit;

namespace Spectralis.Tests.App;

public sealed class PodcastsViewModelTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"pod-vm-{Guid.NewGuid():N}.db");
    private readonly LibraryDatabase _db;

    public PodcastsViewModelTests() => _db = new LibraryDatabase(_dbPath);

    public void Dispose()
    {
        _db.Dispose();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { File.Delete(_dbPath); } catch { }
    }

    private void AddEpisode(string path, string show, string title, uint track = 0, int durationSeconds = 1800)
    {
        _db.Upsert(new TrackInfo
        {
            SourcePath = path,
            Title = title,
            Album = show,
            Artist = show,
            TrackNumber = track,
            Duration = TimeSpan.FromSeconds(durationSeconds),
            FormatName = "MP3",
        }, mtimeTicks: 1, isPodcast: true);
    }

    private PodcastsViewModel MakeVm(Func<IReadOnlyList<string>, int, Task>? play = null) =>
        new(_db, new AppSettings(), play ?? ((_, _) => Task.CompletedTask), () => { });

    [Fact]
    public void RefreshFromDatabase_GroupsEpisodesByShow()
    {
        AddEpisode(@"C:\pod\showA\1.mp3", "Show A", "Ep 1", track: 1);
        AddEpisode(@"C:\pod\showA\2.mp3", "Show A", "Ep 2", track: 2);
        AddEpisode(@"C:\pod\showB\1.mp3", "Show B", "Ep 1", track: 1);

        var vm = MakeVm();

        Assert.Equal(2, vm.Shows.Count);
        Assert.Equal(new[] { "Ep 1", "Ep 2" }, vm.Shows[0].Episodes.Select(e => e.Title));
        Assert.True(vm.HasShows);
    }

    [Fact]
    public void EpisodeRow_StatusReflectsResumeState()
    {
        AddEpisode(@"C:\pod\s\1.mp3", "S", "Fresh");
        AddEpisode(@"C:\pod\s\2.mp3", "S", "InProgress", track: 2);
        AddEpisode(@"C:\pod\s\3.mp3", "S", "Done", track: 3);
        _db.SaveResume(@"C:\pod\s\2.mp3", 600_000, finished: false);
        _db.SaveResume(@"C:\pod\s\3.mp3", 0, finished: true);

        var vm = MakeVm();
        var episodes = vm.Shows.Single().Episodes;

        Assert.Equal("New", episodes[0].StatusText);
        Assert.Equal("20 min left", episodes[1].StatusText);
        Assert.True(episodes[1].HasProgress);
        Assert.Equal("Finished", episodes[2].StatusText);
    }

    [Fact]
    public async Task PlayEpisode_InvokesPlayHookWithEpisodePath()
    {
        AddEpisode(@"C:\pod\s\1.mp3", "S", "Ep");
        IReadOnlyList<string>? played = null;
        var vm = MakeVm((paths, _) => { played = paths; return Task.CompletedTask; });

        await vm.PlayEpisodeAsync(vm.Shows.Single().Episodes.Single());

        Assert.Equal(new[] { @"C:\pod\s\1.mp3" }, played);
    }

    [Fact]
    public void MarkFinished_ThenRefresh_ShowsFinished()
    {
        AddEpisode(@"C:\pod\s\1.mp3", "S", "Ep");
        var vm = MakeVm();

        vm.MarkFinished(vm.Shows.Single().Episodes.Single());

        Assert.Equal("Finished", vm.Shows.Single().Episodes.Single().StatusText);
    }

    [Fact]
    public void RemoveFromPodcasts_DropsTheEpisode()
    {
        AddEpisode(@"C:\pod\s\1.mp3", "S", "Ep");
        var vm = MakeVm();

        vm.RemoveFromPodcasts(vm.Shows.Single().Episodes.Single());

        Assert.False(vm.HasShows);
    }
}
