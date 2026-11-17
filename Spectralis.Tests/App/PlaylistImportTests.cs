using System.Net;
using System.Text.Json;
using Spectralis.App.Services;
using Spectralis.Core.Integrations.Spotify;
using Spectralis.Core.Playlists;
using Xunit;

namespace Spectralis.Tests.App;

public sealed class PlaylistImportTests
{
    private const string Id = "4cOdK2wGLETKBW3PvgPWqT";
    private const string Track = "6eLyGKZI33EAgaRyWJ6FvH";

    [Fact]
    public async Task Spotify_api_reads_modern_items_pages_in_order_with_duplicates()
    {
        var paths = new List<string>();
        using var http = new HttpClient(new Handler(request =>
        {
            paths.Add(request.RequestUri!.PathAndQuery);
            Assert.Equal("test-access", request.Headers.Authorization?.Parameter);
            if (paths.Count == 1) return Ok($$$"""{"id":"{{{Id}}}","name":"Mix","items":{"total":2}}""");
            var next = paths.Count == 2 ? $"https://api.spotify.com/v1/playlists/{Id}/items?offset=1" : null;
            return Ok(JsonSerializer.Serialize(new { total = 2, next, items = new[] { new { item = new { type = "track", uri = $"spotify:track:{Track}", name = "Song", duration_ms = 120000 } } } }));
        }));
        using var spotify = Service(http);
        var result = await spotify.GetPlaylistContentsAsync("client", Id);
        Assert.True(result!.HasFullAccess);
        Assert.Equal(2, result.Tracks.Count);
        Assert.Contains("/items?limit=50", paths[1]);
        Assert.Equal(result.Tracks[0], result.Tracks[1]);
    }

    [Fact]
    public async Task Denied_playlist_contents_preserve_metadata_without_claiming_an_empty_playlist()
    {
        using var http = new HttpClient(new Handler(request => request.RequestUri!.AbsolutePath.EndsWith("/items")
            ? new(HttpStatusCode.Forbidden) : Ok($$$"""{"id":"{{{Id}}}","name":"Public mix","items":{"total":200}}""")));
        using var spotify = Service(http);
        var result = await spotify.GetPlaylistContentsAsync("client", Id);
        Assert.False(result!.HasFullAccess);
        Assert.Equal(200, result.TotalItems);
        Assert.Equal("Public mix", result.Name);
    }

    [Fact]
    public async Task Pagination_never_sends_credentials_to_another_host()
    {
        var calls = 0;
        using var http = new HttpClient(new Handler(_ => ++calls == 1
            ? Ok($$$"""{"id":"{{{Id}}}","name":"Mix","items":{"total":1}}""")
            : Ok("""{"items":[],"next":"https://evil.example/steal"}""")));
        using var spotify = Service(http);
        await Assert.ThrowsAsync<InvalidOperationException>(() => spotify.GetPlaylistContentsAsync("client", Id));
        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task Import_prefers_complete_api_playlist_without_loading_embed()
    {
        var service = new PlaylistImportService(new(), (_, _) => Task.FromResult<SpotifyPlaylistContents?>(
            new(Id, "My mix", null, "snapshot", [new($"spotify:track:{Track}", "Song", "Artist", null, null, 1000)], true, 1)),
            (_, _) => throw new Exception("Embed must not be fetched"));
        var playlist = await service.ImportAsync($"https://open.spotify.com/playlist/{Id}", default);
        Assert.Equal("My mix", playlist.Name);
        Assert.Single(playlist.Items);
        Assert.True(playlist.IsReadOnlySource);
        Assert.Null(playlist.ImportNotice);
    }

    [Fact]
    public async Task Public_fallback_labels_its_potentially_partial_list()
    {
        var entity = new { name = "Public mix", trackList = new[] { new { uri = $"spotify:track:{Track}", title = "Song", subtitle = "Artist", duration = 1000 } } };
        var html = "<script id=\"__NEXT_DATA__\">" + JsonSerializer.Serialize(new { props = new { pageProps = new { state = new { data = new { entity } } } } }) + "</script>";
        var service = new PlaylistImportService(new(), (_, _) => Task.FromResult<SpotifyPlaylistContents?>(null), (_, _) => Task.FromResult(html));
        var playlist = await service.ImportAsync($"spotify:playlist:{Id}", default);
        Assert.Equal("Public mix", playlist.Name);
        Assert.Single(playlist.Items);
        Assert.Contains("incomplete", playlist.ImportNotice);
    }

    [Fact]
    public void Extractor_import_preserves_order_skips_unavailable_and_accepts_null_duration()
    {
        var playlist = PlaylistImportService.ParseExtractorPlaylist("""{"title":"Mix","entries":[{"url":"abc","ie_key":"Youtube","duration":null},null,{"webpage_url":"https://soundcloud.com/a/b","title":"Second"}]}""", "https://youtube.com/playlist?list=x");
        Assert.Equal(2, playlist.Items.Count);
        Assert.Equal("https://www.youtube.com/watch?v=abc", playlist.Items[0].SourceUrl);
        Assert.Equal("Second", playlist.Items[1].Title);
        Assert.Contains("1 unavailable", playlist.ImportNotice);
    }

    [Fact]
    public void M3u_preserves_web_and_spotify_references()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".m3u8");
        try
        {
            File.WriteAllLines(path, ["#EXTM3U", "https://suno.com/song/example", $"spotify:track:{Track}"]);
            Assert.Equal(new[] { "https://suno.com/song/example", $"spotify:track:{Track}" }, M3uParser.Import(path));
        }
        finally { File.Delete(path); }
    }

    private static HttpResponseMessage Ok(string json) => new(HttpStatusCode.OK) { Content = new StringContent(json) };
    private static SpotifyService Service(HttpClient http) => new(new SpotifyTokenStore { AccessToken = "test-access", RefreshToken = "test-refresh", ExpiresAt = DateTime.UtcNow.AddHours(1) }, http);
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> handle) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => Task.FromResult(handle(request));
    }
}
