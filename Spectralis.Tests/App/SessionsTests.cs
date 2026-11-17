using System.Reactive.Linq;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Spectralis.App.Services;
using Spectralis.App.ViewModels;
using Spectralis.App.Views;
using Spectralis.Core.Audio;
using Spectralis.Core.SongWars;
using Spectralis.Tests.Core;
using Xunit;

namespace Spectralis.Tests.App;

[Collection("Visualizer UI")]
public sealed class SessionsTests
{
    [AvaloniaFact]
    public async Task RoundKeepsDownloadedAudioUntilListeningResumes()
    {
        var folder = Path.Combine(Path.GetTempPath(), "Spectralis", "remote-audio");
        Directory.CreateDirectory(folder);
        var original = Path.Combine(folder, Guid.NewGuid().ToString("N") + ".wav");
        var fixture = WavFixture.CreateSineWav(2, 48000, 2);
        File.Copy(fixture, original);
        try
        {
            using var engine = new AudioEngine(new FakeAudioDeviceEnumerator());
            using var player = new NowPlayingViewModel(engine, enablePositionPolling: false);
            var track = Spectralis.Core.Metadata.TrackMetadataReader.Read(original);
            await player.LoadPreparedTrackAsync(original, track, false, ownsTemporaryFile: true);
            await player.SuspendSessionQueueAsync();
            await player.PlayQueueAsync([fixture], 0);
            Assert.True(File.Exists(original));
            await player.RestoreSessionQueueAsync();
            Assert.Equal(original, engine.CurrentTrack?.SourcePath);
            Assert.True(File.Exists(original));
        }
        finally { File.Delete(original); File.Delete(fixture); }
    }

    [AvaloniaFact]
    public async Task RoundRestoresQueuePositionRepeatAndPausedState()
    {
        var first = WavFixture.CreateSineWav(4, 48000, 2);
        var second = WavFixture.CreateSineWav(4, 48000, 2);
        var round = WavFixture.CreateSineWav(4, 48000, 2);
        try
        {
            using var engine = new AudioEngine(new FakeAudioDeviceEnumerator());
            using var player = new NowPlayingViewModel(engine, enablePositionPolling: false);
            await player.PlayQueueAsync([first, second], 1, startPlayback: false);
            engine.Seek(0.5f);
            player.Queue.Repeat = RepeatMode.All;
            await player.SuspendSessionQueueAsync();
            await player.PlayQueueAsync([round], 0);
            await player.PlayNextAsync();
            Assert.Equal(round, engine.CurrentTrack?.SourcePath);
            await player.RestoreSessionQueueAsync();
            Assert.Equal(new[] { first, second }, player.Queue.Items);
            Assert.Equal(1, player.Queue.CurrentIndex);
            Assert.Equal(second, engine.CurrentTrack?.SourcePath);
            Assert.InRange(engine.GetPosition(), 0.49, 0.51);
            Assert.False(engine.IsPlaying);
            Assert.Equal(RepeatMode.All, player.Queue.Repeat);
            Assert.False(player.SessionRoundActive);
        }
        finally { File.Delete(first); File.Delete(second); File.Delete(round); }
    }

    [AvaloniaFact]
    public async Task TournamentAndVotesSurviveViewReplacementAndReload()
    {
        var root = Path.Combine(Path.GetTempPath(), "sessions-test-" + Guid.NewGuid().ToString("N"));
        var store = new SongWarsTournamentStore(root);
        try
        {
            var released = 0;
            var vm = new SongWarsViewModel(store) { PlayRoundTrack = _ => Task.CompletedTask, EndRound = () => { released++; return Task.CompletedTask; } };
            vm.AddEntry("First", "Artist", "https://example.com/a.mp3");
            vm.AddEntry("Second", "Artist", "https://example.com/b.mp3");
            await vm.CreateCommand.Execute();
            var view = new SongWarsView { DataContext = vm };
            Assert.Same(vm.Session, view.CurrentSession);
            await vm.PlayACommand.Execute();
            await vm.PauseCommand.Execute();
            Assert.True(vm.IsPaused);
            await vm.ResumeCommand.Execute();
            Assert.False(vm.IsPaused);
            await vm.PlayBCommand.Execute();
            await vm.VoteCommand.Execute();
            await vm.Judges[0].VoteACommand.Execute();
            Assert.Contains("1 / 1", vm.Tally);
            var resumed = new SongWarsViewModel(store) { SavedId = vm.Tournament!.TournamentId, EndRound = vm.EndRound };
            await resumed.LoadCommand.Execute();
            Assert.Contains("1 / 1", resumed.Tally);
            Assert.True(resumed.CanVote);
            await resumed.RevealCommand.Execute();
            Assert.Equal(1, released);
            Assert.NotEmpty(resumed.Results);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [AvaloniaTheory]
    [InlineData(760)]
    [InlineData(1200)]
    public void WorkspaceLoadsAtCompactAndWideWidths(int width)
    {
        using var listening = new SharedPlayViewModel();
        using var requests = new StreamerQueueViewModel();
        using var engine = new AudioEngine(new FakeAudioDeviceEnumerator());
        using var player = new NowPlayingViewModel(engine, enablePositionPolling: false);
        var settings = new AppSettings();
        using var vm = new SessionsViewModel(settings, listening, requests, new SongWarsViewModel(settings), player, _ => Task.CompletedTask, _ => Task.CompletedTask);
        var view = new SessionsView { DataContext = vm };
        var window = new Window { Content = view, Width = width, Height = 900 };
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            Assert.True(view.Bounds.Width > 0);
            Assert.Equal(0, Grid.GetRow(view.FindControl<StackPanel>("InboxPanel")!));
            Assert.Equal(width < 860 ? 1 : 0, Grid.GetRow(view.FindControl<StackPanel>("RoundPanel")!));
        }
        finally { window.Close(); }
    }

}
