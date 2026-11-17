using Avalonia.Headless.XUnit;
using Spectralis.App.Services;
using Spectralis.App.ViewModels;
using Spectralis.Core.Audio;
using Spectralis.Core.Playlists;
using Spectralis.Tests.Core;
using Xunit;

namespace Spectralis.Tests.App;

[Collection("Visualizer UI")]
public sealed class QueuePrecacheTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "spectralis-precache-" + Guid.NewGuid().ToString("N"));
    private readonly string wav = WavFixture.CreateSineWav(3);
    private const string Link = "https://soundcloud.com/example/song";

    [Fact]
    public void Preloading_defaults_on_and_opt_out_survives_cloning()
    {
        Assert.True(new AppSettings().PrecacheUpcomingAudio);
        Assert.False(new AppSettings { PrecacheUpcomingAudio = false }.Clone().PrecacheUpcomingAudio);
    }

    [Fact]
    public async Task Concurrent_cache_requests_share_one_download_and_restart_reuses_audio()
    {
        var sources = new Sources(wav);
        var cache = new QueueAudioCache(root, sources);
        var both = await Task.WhenAll(cache.AcquireAsync(new(Link), null, default), cache.AcquireAsync(new(Link), null, default));
        using var a = both[0];
        using var b = both[1];
        using var restarted = new QueueAudioCache(root, new Sources(wav) { Fail = true }).TryAcquire(Link);
        Assert.Equal(1, sources.Calls);
        Assert.Equal(a.Path, b.Path);
        Assert.Equal(a.Path, restarted?.Path);
        Assert.NotEmpty(File.ReadAllBytes(a.Path));
    }

    [Fact]
    public async Task Cache_prunes_released_audio_without_evicting_active_tracks()
    {
        var cache = new QueueAudioCache(root, new Sources(wav), new FileInfo(wav).Length * 2 + 1);
        using var first = await cache.AcquireAsync(new(Link), null, default);
        using var second = await cache.AcquireAsync(new(Link + "2"), null, default);
        var third = await cache.AcquireAsync(new(Link + "3"), null, default);
        Assert.True(File.Exists(first.Path));
        Assert.True(File.Exists(second.Path));
        third.Dispose();
        Assert.False(File.Exists(third.Path));
        Assert.True(File.Exists(first.Path));
        Assert.True(File.Exists(second.Path));
    }

    [AvaloniaFact]
    public async Task Automatic_remote_handoff_keeps_the_same_audio_device_and_position()
    {
        var sources = new Sources(wav);
        var cache = new QueueAudioCache(root, sources);
        var devices = new FakeAudioDeviceEnumerator();
        using var engine = new AudioEngine(devices);
        using var vm = new NowPlayingViewModel(engine, enablePositionPolling: false, queueAudioCache: cache);
        var ready = new TaskCompletionSource();
        vm.UpcomingQueueTrackReady += _ => ready.TrySetResult();
        await vm.PlayQueueAsync([wav, Link], 0);
        await ready.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var device = devices.Current!;
        var initializations = device.InitCount;
        // Pump through the end of the first song and into its preloaded successor.
        var samples = (int)(device.SampleRate * device.Channels * 3.25);
        for (var i = 0; i < samples; i += 512) Assert.NotEmpty(device.Pump(512));
        await Eventually(() => vm.Queue.CurrentPath == Link && vm.CurrentRemoteAudio is not null);
        Assert.Single(devices.CreatedDevices);
        Assert.Equal(initializations, device.InitCount);
        Assert.True(engine.GetPosition() > 0.1);
        Assert.Empty(vm.LoadError);
    }

    [AvaloniaFact]
    public async Task Queue_preloads_remote_next_and_keeps_queue_on_cached_handoff()
    {
        var sources = new Sources(wav);
        var cache = new QueueAudioCache(root, sources);
        using var engine = new AudioEngine(new FakeAudioDeviceEnumerator());
        using var vm = new NowPlayingViewModel(engine, enablePositionPolling: false, queueAudioCache: cache);
        await vm.PlayQueueAsync([wav, Link, wav], 0);
        await Eventually(() => cache.TryAcquire(Link) is { } audio && Release(audio));
        await vm.PlayNextAsync();
        Assert.Equal(3, vm.Queue.Count);
        Assert.Equal(Link, vm.Queue.CurrentPath);
        Assert.Equal(Path.GetFullPath(root), Path.GetDirectoryName(engine.CurrentTrack!.SourcePath));
        Assert.Empty(vm.LoadError);
        Assert.True(engine.IsPlaying);
        Assert.Equal(1, sources.Calls);
    }

    [AvaloniaFact]
    public async Task Opt_out_cancels_pending_download_and_does_not_start_another()
    {
        var sources = new Sources(wav) { Wait = true };
        var cache = new QueueAudioCache(root, sources);
        using var engine = new AudioEngine(new FakeAudioDeviceEnumerator());
        using var vm = new NowPlayingViewModel(engine, enablePositionPolling: false, queueAudioCache: cache);
        await vm.PlayQueueAsync([wav, Link], 0);
        await sources.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        vm.PrecacheUpcomingAudio = false;
        await sources.Cancelled.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Null(cache.TryAcquire(Link));
        await vm.PlayQueueAsync([wav, Link + "2"], 0);
        Assert.Equal(1, sources.Calls);
    }

    [AvaloniaFact]
    public async Task Skipping_early_reuses_the_in_progress_download()
    {
        var gate = new TaskCompletionSource();
        var sources = new Sources(wav) { Block = gate };
        using var engine = new AudioEngine(new FakeAudioDeviceEnumerator());
        using var vm = new NowPlayingViewModel(engine, enablePositionPolling: false, queueAudioCache: new(root, sources));
        await vm.PlayQueueAsync([wav, Link], 0);
        await sources.Started.Task;
        var skip = vm.PlayNextAsync();
        Assert.False(skip.IsCompleted);
        gate.SetResult();
        await skip.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(1, sources.Calls);
        Assert.Equal(Link, vm.Queue.CurrentPath);
        Assert.Empty(vm.LoadError);
    }

    [Fact]
    public async Task Cancellation_checkpoints_completed_offline_tracks()
    {
        using var cancellation = new CancellationTokenSource();
        var playlist = new Playlist { Items = [new() { SourceUrl = Link }, new() { SourceUrl = Link + "2" }] };
        var service = new PlaylistOfflineService(new QueueAudioCache(Path.Combine(root, "cache"), new Sources(wav)));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.DownloadAsync(playlist, root,
            p => { if (p.Items[0].OfflinePath is not null) cancellation.Cancel(); }, null, cancellation.Token));
        Assert.NotNull(playlist.Items[0].PlayableReference(true));
        Assert.Null(playlist.Items[1].PlayableReference(true));
        Assert.Empty(Directory.GetFiles(root, "*.partial", SearchOption.AllDirectories));
        var result = await service.DownloadAsync(playlist, root, _ => { }, null, default);
        Assert.Equal(2, result.Downloaded);
    }

    [AvaloniaFact]
    public async Task Replacing_the_queue_cancels_stale_preparation()
    {
        var sources = new Sources(wav) { Wait = true };
        using var engine = new AudioEngine(new FakeAudioDeviceEnumerator());
        using var vm = new NowPlayingViewModel(engine, enablePositionPolling: false, queueAudioCache: new(root, sources));
        await vm.PlayQueueAsync([wav, Link], 0);
        await sources.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await vm.PlayQueueAsync([wav], 0);
        await sources.Cancelled.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Single(vm.Queue.Items);
    }

    [Fact]
    public async Task Offline_download_resumes_and_exports_only_local_audio()
    {
        var sources = new Sources(wav);
        var cache = new QueueAudioCache(Path.Combine(root, "cache"), sources);
        var playlist = new Playlist { Name = "../mix: songs", Items = [new() { SourceUrl = Link, Title = "../same" }, new() { SourceUrl = Link + "2", Title = "../same" }] };
        var service = new PlaylistOfflineService(cache);
        var saves = 0;
        var result = await service.DownloadAsync(playlist, root, _ => saves++, null, default);
        Assert.Equal(2, result.Downloaded);
        Assert.Empty(result.Failures);
        Assert.NotEqual(playlist.Items[0].OfflinePath, playlist.Items[1].OfflinePath);
        Assert.All(playlist.Items, item => Assert.True(File.Exists(item.PlayableReference(true))));
        Assert.DoesNotContain("https:", File.ReadAllText(Path.Combine(playlist.OfflineDirectory!, "playlist.m3u8")));
        await service.DownloadAsync(playlist, root, _ => { }, null, default);
        Assert.Equal(2, sources.Calls);
        Assert.True(saves >= 2);
        File.Delete(playlist.Items[0].OfflinePath!);
        Assert.Null(playlist.Items[0].PlayableReference(true));
        Assert.Equal(Link, playlist.Items[0].PlayableReference());
    }

    [Fact]
    public async Task Failed_download_keeps_completed_tracks_and_reports_partial_result()
    {
        var sources = new Sources(wav) { FailAfter = 1 };
        var playlist = new Playlist { Items = [new() { SourceUrl = Link }, new() { SourceUrl = Link + "2" }] };
        var result = await new PlaylistOfflineService(new QueueAudioCache(Path.Combine(root, "cache"), sources))
            .DownloadAsync(playlist, root, _ => { }, null, default);
        Assert.Equal(1, result.Downloaded);
        Assert.Single(result.Failures);
        Assert.NotNull(playlist.Items[0].PlayableReference(true));
        Assert.Null(playlist.Items[1].PlayableReference(true));
        Assert.Empty(Directory.GetFiles(root, "*.partial", SearchOption.AllDirectories));
    }

    [Fact]
    public void Shuffle_repeat_peek_matches_the_track_that_will_play()
    {
        var queue = new PlayQueue { Repeat = RepeatMode.All };
        queue.AddRange(["one", "two", "three"]);
        queue.SetCurrent(0);
        queue.Shuffle = true;
        for (var i = 0; i < 30; i++)
        {
            var next = queue.PeekNextPath();
            Assert.Equal(next, queue.PeekNextPath());
            Assert.Equal(next, queue.MoveNext());
        }
    }

    private static bool Release(CachedQueueAudio audio) { audio.Dispose(); return true; }
    private static async Task Eventually(Func<bool> check)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!check()) await Task.Delay(10, timeout.Token);
    }

    private sealed class Sources(string path) : IDjSourceService
    {
        public int Calls;
        public bool Fail, Wait;
        public TaskCompletionSource? Block;
        public int FailAfter = int.MaxValue;
        public TaskCompletionSource Started = new();
        public TaskCompletionSource Cancelled = new();
        public async Task<DjPreparedSource> PrepareAsync(DjSource source, IProgress<string>? progress, CancellationToken ct)
        {
            Calls++;
            Started.TrySetResult();
            if (Block is not null) await Block.Task.WaitAsync(ct);
            if (Wait)
            {
                try { await Task.Delay(Timeout.Infinite, ct); }
                catch (OperationCanceledException) { Cancelled.TrySetResult(); throw; }
            }
            if (Fail || Calls > FailAfter) throw new IOException("Source unavailable");
            return new(path, source.Reference, "Song", "Artist", "Test");
        }
    }

    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); File.Delete(wav); }
}
