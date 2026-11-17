using System.Net;
using System.Text.Json;
using Spectralis.App.Services;
using Spectralis.Core.Integrations.Spotify;
using Spectralis.Tests.Core;
using Xunit;

namespace Spectralis.Tests.App;

public sealed class SpotifyMatchupServiceTests : IDisposable
{
    private const string Id = "4cOdK2wGLETKBW3PvgPWqT";
    private static readonly SpotifyTrackFacts Facts = new(Id, "Never Gonna Give You Up", "Rick Astley", 213000);
    private static readonly YouTubeCandidate Candidate = new("dQw4w9WgXcQ", Facts.Title, "Rick Astley", 213);
    private readonly string _root = Path.Combine(Path.GetTempPath(), "spectralis-matchup-service-" + Guid.NewGuid().ToString("N"));
    private readonly string _fixture = WavFixture.CreateSineWav(1);

    public SpotifyMatchupServiceTests() => Directory.CreateDirectory(_root);
    public void Dispose() { Directory.Delete(_root, true); File.Delete(_fixture); }

    private Task<string> Download(string _, string id, YouTubeCandidate match, Action<string>? status, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        File.Copy(_fixture, Path.Combine(_root, id + ".wav"), overwrite: true);
        return Task.FromResult(id + ".wav");
    }

    private SpotifyMatchupService Service(
        Func<string, CancellationToken, Task<SpotifyTrackFacts?>>? fetch = null,
        Func<string, SpotifyTrackFacts, CancellationToken, Task<YouTubeCandidate?>>? search = null,
        Func<string, string, YouTubeCandidate, Action<string>?, CancellationToken, Task<string>>? download = null) =>
        new(_root, () => "test-resolver", fetch ?? ((_, _) => Task.FromResult<SpotifyTrackFacts?>(Facts)),
            search ?? ((_, _, _) => Task.FromResult<YouTubeCandidate?>(Candidate)), download ?? Download);

    private Task SaveMapping(string videoId = "dQw4w9WgXcQ") => File.WriteAllTextAsync(Path.Combine(_root, "index.json"),
        JsonSerializer.Serialize(new[] { new SpotifyMatchupService.CachedMatch(Id, videoId, Id + ".wav",
            Facts.Title, Facts.Artist, null, null, Facts.DurationMs, DateTime.UtcNow) }));

    [Fact]
    public async Task Complete_known_facts_skip_the_spotify_page_lookup()
    {
        var service = Service(fetch: (_, _) => throw new Exception("Already have the track details"));
        var result = await service.ResolveAsync(Id, Facts, null, default);
        Assert.Equal(Facts.Title, result?.Title);
        Assert.True(File.Exists(result?.CachedAudioPath));
    }

    [Fact]
    public async Task Replaying_a_cached_track_needs_no_metadata_search_or_download()
    {
        await Service().ResolveAsync(Id, Facts, null, default);
        var service = new SpotifyMatchupService(_root, () => throw new Exception("No helper needed"),
            (_, _) => throw new Exception("No page needed"), (_, _, _) => throw new Exception("No search needed"),
            (_, _, _, _, _) => throw new Exception("No download needed"));
        var result = await service.ResolveAsync(Id, null, null, default);
        Assert.Equal(Path.Combine(_root, Id + ".wav"), result?.CachedAudioPath);
    }

    [Fact]
    public async Task Evicted_audio_reuses_its_mapping_without_another_page_lookup_or_search()
    {
        await SaveMapping();
        var service = Service(fetch: (_, _) => throw new Exception("Have saved facts"),
            search: (_, _, _) => throw new Exception("Have a verified match"));
        Assert.NotNull(await service.ResolveAsync(Id, null, null, default));
    }

    [Fact]
    public async Task Removed_upload_searches_again_once_instead_of_leaving_a_dead_mapping()
    {
        await SaveMapping("oldVideo123");
        var attempts = new List<string>();
        var service = Service(download: (exe, id, match, status, ct) =>
        {
            attempts.Add(match.Id);
            if (match.Id == "oldVideo123") throw new InvalidOperationException("Video unavailable");
            return Download(exe, id, match, status, ct);
        });
        Assert.NotNull(await service.ResolveAsync(Id, null, null, default));
        Assert.Equal(new[] { "oldVideo123", Candidate.Id }, attempts);
    }

    [Fact]
    public async Task Two_service_instances_loading_the_same_track_only_search_and_download_once()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var searches = 0; var downloads = 0;
        Task<YouTubeCandidate?> Search(string _, SpotifyTrackFacts facts, CancellationToken ct)
        { Interlocked.Increment(ref searches); return Task.FromResult<YouTubeCandidate?>(Candidate); }
        async Task<string> Get(string exe, string id, YouTubeCandidate match, Action<string>? status, CancellationToken ct)
        {
            Interlocked.Increment(ref downloads); entered.TrySetResult();
            await release.Task.WaitAsync(ct);
            return await Download(exe, id, match, status, ct);
        }
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var first = Service(search: Search, download: Get).ResolveAsync(Id, Facts, null, timeout.Token);
        await entered.Task.WaitAsync(timeout.Token);
        var second = Service(search: Search, download: Get).ResolveAsync(Id, Facts, null, timeout.Token);
        release.SetResult();
        var results = await Task.WhenAll(first, second);
        Assert.Equal(1, searches); Assert.Equal(1, downloads);
        Assert.Equal(results[0]?.CachedAudioPath, results[1]?.CachedAudioPath);
    }

    [Fact]
    public async Task Cancelling_a_waiting_deck_does_not_cancel_the_other_decks_download()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var owner = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var waiter = new CancellationTokenSource();
        var service = Service(download: async (exe, id, match, status, ct) =>
        {
            entered.SetResult(); await release.Task.WaitAsync(ct);
            return await Download(exe, id, match, status, ct);
        });
        var first = service.ResolveAsync(Id, Facts, null, owner.Token);
        await entered.Task.WaitAsync(owner.Token);
        var second = service.ResolveAsync(Id, Facts, null, waiter.Token);
        waiter.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => second);
        Assert.False(owner.IsCancellationRequested);
        release.SetResult();
        Assert.NotNull(await first);
    }

    [Fact]
    public async Task Public_playlist_details_are_reused_for_track_matching_without_an_api_request()
    {
        var html = "<script id=\"__NEXT_DATA__\">" +
            "{\"props\":{\"pageProps\":{\"state\":{\"data\":{\"entity\":{" +
            "\"type\":\"playlist\",\"trackList\":[{\"uri\":\"spotify:track:" + Id +
            "\",\"title\":\"Never Gonna Give You Up\",\"subtitle\":\"Rick Astley\",\"duration\":213000}]}}}}}}</script>";
        var requests = new Requests(html);
        using var http = new HttpClient(requests);
        var service = new SpotifyMatchupService(_root, () => "test-resolver", null,
            (_, facts, _) => { Assert.Equal(Facts, facts); return Task.FromResult<YouTubeCandidate?>(Candidate); }, Download, http);
        Assert.True(SpotifyPlaybackLink.TryParse("spotify:playlist:" + Id, out var link));
        Assert.Single(await service.FetchContextTracksAsync(link, default));
        Assert.NotNull(await service.ResolveAsync(Id, null, null, default));
        Assert.Equal(new[] { "/embed/playlist/" + Id }, requests.Paths);
    }

    [Fact]
    public async Task Uncertain_match_returns_no_audio_and_does_not_download()
    {
        var service = Service(search: (_, _, _) => Task.FromResult<YouTubeCandidate?>(null),
            download: (_, _, _, _, _) => throw new Exception("No confident match"));
        Assert.Null(await service.ResolveAsync(Id, Facts, null, default));
    }

    private sealed class Requests(string html) : HttpMessageHandler
    {
        public List<string> Paths { get; } = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Assert.Equal("open.spotify.com", request.RequestUri?.Host);
            Paths.Add(request.RequestUri!.AbsolutePath);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(html) });
        }
    }
}
