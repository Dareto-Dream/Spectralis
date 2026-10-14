using Spectralis.App.Services;
using Spectralis.App.ViewModels;
using Spectralis.Core.Audio.Dj;
using Spectralis.Tests.Core;
using Xunit;

namespace Spectralis.Tests.App;

public sealed class DjSourceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "spectralis-source-test-" + Guid.NewGuid().ToString("N"));
    public DjSourceTests() => Directory.CreateDirectory(_root);
    public void Dispose() => Directory.Delete(_root, true);
    private string Audio()
    {
        var path = WavFixture.CreateSineWav(1, 48000, 2);
        var target = Path.Combine(_root, Guid.NewGuid() + ".wav"); File.Move(path, target); return target;
    }
    private static RemoteAudioResolveResult Result(string? cache = null, RemoteAudioServiceKind kind = RemoteAudioServiceKind.DirectAudio) =>
        new(kind, "Direct audio", "https://example.test/song.m4a", "https://cdn.example.test/audio", ".m4a", "M4A",
            "Night Drive", "Artist", null, null, TimeSpan.FromSeconds(1), "https://example.test", CachedAudioPath: cache);
    private DjViewModel Model(IDjSourceService sources) => new(new() { DjModeEnabled = true },
        mixer: new(new FakeAudioDeviceEnumerator()), cues: new(Path.Combine(_root, "cues.json")), saveSettings: () => { }, sources: sources);

    [Theory]
    [InlineData("https://open.spotify.com/track/123")]
    [InlineData("https://spotify.link/test")]
    [InlineData("https://spoti.fi/test")]
    [InlineData("spotify:track:123")]
    public async Task Spotify_is_rejected_before_network_or_cache_access(string url)
    {
        var calls = 0;
        var service = new DjSourceService((_, _) => { calls++; throw new Exception("Network must not run"); });
        var error = await Assert.ThrowsAsync<NotSupportedException>(() => service.PrepareAsync(new(url, Audio()), null, default));
        Assert.Contains("Coming soon", error.Message); Assert.Equal(0, calls);
    }

    [Fact]
    public async Task Borrowing_now_playing_holds_a_lease_but_does_not_delete_its_cache()
    {
        var path = Audio(); var releases = 0;
        var service = new DjSourceService((_, _) => throw new Exception("Must reuse cache"), release: _ => releases++);
        var source = await service.PrepareAsync(new("https://example.test/song", path, "Shared track"), null, default);
        Assert.Equal(path, source.Path); Assert.Equal("Shared track", source.Title);
        Assert.Throws<IOException>(() => { using var exclusive = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None); });
        source.Dispose(); source.Dispose();
        Assert.True(File.Exists(path)); Assert.Equal(0, releases);
        using var writable = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
    }

    [Fact]
    public async Task Download_preserves_resolver_options_and_cleans_owned_cache_once()
    {
        var path = Audio(); var result = Result(); var releases = 0;
        var service = new DjSourceService((_, _) => Task.FromResult(result), (resolved, _, _) =>
        { Assert.Same(result, resolved); Assert.Equal("https://example.test", resolved.RefererUrl); return Task.FromResult(path); },
            p => { releases++; File.Delete(p); });
        var source = await service.PrepareAsync(new(result.SourceUrl), null, default);
        Assert.Equal(result.SourceUrl, source.Identity); Assert.Equal("Night Drive", source.Title);
        Assert.True(File.Exists(path)); source.Dispose(); source.Dispose();
        Assert.False(File.Exists(path)); Assert.Equal(1, releases);
    }

    [Fact]
    public async Task Cancelled_resolve_releases_a_returned_cache()
    {
        var path = Audio(); using var cancellation = new CancellationTokenSource();
        var service = new DjSourceService((_, _) => { cancellation.Cancel(); return Task.FromResult(Result(path)); }, release: File.Delete);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.PrepareAsync(new(Result().SourceUrl), null, cancellation.Token));
        Assert.False(File.Exists(path));
    }

    [Fact]
    public async Task Redirected_spotify_and_metadata_only_results_never_become_deck_audio()
    {
        var service = new DjSourceService((_, _) => Task.FromResult(Result(kind: RemoteAudioServiceKind.Spotify)), (_, _, _) => throw new Exception("Must not download"));
        await Assert.ThrowsAsync<NotSupportedException>(() => service.PrepareAsync(new("https://example.test/redirect"), null, default));
        service = new DjSourceService((_, _) => Task.FromResult(Result() with { MetadataOnly = true }), (_, _, _) => throw new Exception("Must not download"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.PrepareAsync(new("https://example.test/redirect"), null, default));
    }

    [Fact]
    public async Task Real_direct_url_resolver_and_http_cache_feed_a_seekable_deck()
    {
        var bytes = File.ReadAllBytes(Audio());
        using var server = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        server.Start();
        var url = $"http://127.0.0.1:{((System.Net.IPEndPoint)server.LocalEndpoint).Port}/Party.wav";
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var serving = Task.Run(async () =>
        {
            // The shared resolver checks redirects before the cache downloads audio.
            for (var attempt = 0; attempt < 2; attempt++)
            {
            using var client = await server.AcceptTcpClientAsync(timeout.Token);
            await using var stream = client.GetStream();
            using var reader = new StreamReader(stream, leaveOpen: true);
            var request = new List<string>();
            while (await reader.ReadLineAsync(timeout.Token) is { Length: > 0 } line) request.Add(line);
            Assert.StartsWith("GET /Party.wav ", request[0]);
            if (attempt == 1) Assert.Contains(request, h => h.Equals("Range: bytes=0-", StringComparison.OrdinalIgnoreCase));
            var header = System.Text.Encoding.ASCII.GetBytes($"HTTP/1.1 200 OK\r\nContent-Type: audio/wav\r\nContent-Length: {bytes.Length}\r\nConnection: close\r\n\r\n");
            await stream.WriteAsync(header, timeout.Token);
            if (attempt == 1) await stream.WriteAsync(bytes, timeout.Token);
            }
        }, timeout.Token);
        var source = await new DjSourceService().PrepareAsync(new(url), null, timeout.Token);
        var cache = source.Path;
        using var vm = Model(new DjSourceService());
        try
        {
            Assert.Equal("Party", source.Title);
            await vm.DeckA.LoadSourcesAsync([new(url, cache, source.Title)]);
        }
        finally { source.Dispose(); }
        await serving;
        Assert.False(File.Exists(cache)); Assert.True(vm.DeckA.HasTrack);
        vm.Mixer.Seek(0, .5); vm.Mixer.Transport(0, DjTransport.PlayPause);
        var buffer = new float[4800]; vm.Mixer.Read(buffer, 0, buffer.Length);
        Assert.InRange(vm.Mixer.GetDeck(0).Position, .549, .551);
        Assert.Contains(buffer, sample => Math.Abs(sample) > .01);
    }

    [Fact]
    public async Task Url_deck_keeps_title_and_cues_after_download_is_released_and_reloaded()
    {
        var downloads = new List<string>();
        var service = new DjSourceService((_, _) => { var path = Audio(); downloads.Add(path); return Task.FromResult(Result(path)); }, release: File.Delete);
        using var vm = Model(service);
        await vm.DeckA.LoadSourcesAsync([new(Result().SourceUrl)]);
        Assert.True(vm.DeckA.HasTrack); Assert.Equal("Night Drive", vm.DeckA.Title);
        Assert.Equal("Artist", vm.DeckA.Artist); Assert.Equal("Night Drive", vm.DeckA.Stems[0].Name);
        Assert.False(File.Exists(downloads[0])); Assert.True(vm.HasSession);
        vm.Mixer.ConfigureDeck(0, new(Bpm: 120, BeatOffset: 0)); vm.Mixer.Seek(0, .5);
        vm.Mixer.Transport(0, DjTransport.SetCue);
        new DjCueStore(Path.Combine(_root, "cues.json")).Save(vm.Mixer.GetDeck(0));
        using var reloaded = Model(service);
        await reloaded.DeckB.LoadFilesAsync([Result().SourceUrl]);
        Assert.Equal(.5, reloaded.Mixer.GetDeck(1).Cue, 6);
        reloaded.Mixer.Transport(1, DjTransport.PlayPause);
        var buffer = new float[9600]; reloaded.Mixer.Read(buffer, 0, buffer.Length);
        Assert.Contains(buffer, sample => Math.Abs(sample) > .01); Assert.All(downloads, p => Assert.False(File.Exists(p)));
    }

    [Fact]
    public async Task Failed_or_cancelled_replacement_preserves_the_previous_track_and_releases_busy()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var service = new DjSourceService(async (_, ct) =>
        { started.SetResult(); await Task.Delay(Timeout.Infinite, ct); return Result(); });
        using var vm = Model(service); var path = Audio();
        await vm.DeckA.LoadFilesAsync([path]);
        var loading = vm.DeckA.LoadFilesAsync([Result().SourceUrl]);
        await started.Task; Assert.True(vm.DeckA.Busy); vm.DeckA.CancelLoad(); await loading;
        Assert.False(vm.DeckA.Busy); Assert.Equal(path, vm.Mixer.GetDeck(0).Path);
        await vm.DeckA.LoadFilesAsync(["https://open.spotify.com/track/test"]);
        Assert.Contains("Coming soon", vm.Status); Assert.Equal(path, vm.Mixer.GetDeck(0).Path);
    }

    [Fact]
    public void Queue_browser_uses_shared_queue_and_search_without_copying_it_to_library()
    {
        using var vm = Model(new DjSourceService());
        var queue = new List<DjLibraryTrack> { new("https://example.test/one", "One", "Artist", ""), new("/two.wav", "Two", "Other", "") };
        vm.QueueSource = () => queue; vm.BrowserSource = "Queue";
        Assert.Equal(2, vm.LibraryTracks.Count); vm.Search = "Artist";
        Assert.Single(vm.LibraryTracks); Assert.Equal("One", vm.LibraryTracks[0].Title);
        queue.Clear(); vm.RefreshLibrary(); Assert.Empty(vm.LibraryTracks);
    }
}
