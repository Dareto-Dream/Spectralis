using System.Net;
using Spectralis.App.Services;
using Xunit;

namespace Spectralis.Tests.App;

public sealed class YouTubeResolveTests
{
    [Fact]
    public async Task PreviewUsesOnlyOembedAndDoesNotFetchTheWatchPageOrRunYtDlp()
    {
        var calls = new List<Uri>();
        using var http = new HttpClient(new Handler(request =>
        {
            calls.Add(request.RequestUri!);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("""
                {"title":"Tokyo", "author_name":"Moe Sound", "thumbnail_url":"https://i.ytimg.com/vi/WmdAk2zyQkU/hqdefault.jpg"}
                """) };
        }));
        var result = await new OpenUrlService(http).ResolveAsync(
            "https://www.youtube.com/watch?v=WmdAk2zyQkU&list=RDWmdAk2zyQkU&start_radio=1", CancellationToken.None, quickOnly: true);
        Assert.Equal("Tokyo", result.Title);
        Assert.Equal("Moe Sound", result.Artist);
        Assert.True(result.MetadataOnly);
        Assert.Null(result.CachedAudioPath);
        Assert.Single(calls);
        Assert.Equal("/oembed", calls[0].AbsolutePath);
        Assert.DoesNotContain("start_radio", calls[0].Query);
    }

    [Fact]
    public async Task DirectAudioDoesNotMakeAnExtraPageRequest()
    {
        using var http = new HttpClient(new Handler(_ => throw new Exception("Unexpected HTTP request")));
        var result = await new OpenUrlService(http).ResolveAsync("https://audio.example.test/song.mp3", CancellationToken.None);
        Assert.Equal(RemoteAudioServiceKind.DirectAudio, result.Kind);
        Assert.Equal("https://audio.example.test/song.mp3", result.AudioUrl);
    }

    [Fact]
    public async Task ClipboardSpotifyEnrichmentLoadsThePublicTitleAndCoverWithoutChangingPlayback()
    {
        using var http = new HttpClient(new Handler(request =>
        {
            Assert.Equal("/oembed", request.RequestUri!.AbsolutePath);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("""
                {"title":"A song", "thumbnail_url":"https://i.scdn.co/image/cover"}
                """) };
        }));
        var result = await new OpenUrlService(http).ResolveClipboardMetadataAsync(
            "spotify:track:6eLyGKZI33EAgaRyWJ6FvH", CancellationToken.None);
        Assert.Equal("A song", result.Title);
        Assert.Equal("https://i.scdn.co/image/cover", result.ArtworkUrl);
        Assert.Equal("spotify:track:6eLyGKZI33EAgaRyWJ6FvH", result.ExternalId);
        Assert.False(result.IsWebViewFallback());
    }

    [Fact]
    public void PlaybackDownloadsAndReadsMetadataInOneInvocationWithLiteralArguments()
    {
        const string url = "https://www.youtube.com/watch?v=WmdAk2zyQkU&list=radio";
        var arguments = OpenUrlService.BuildYtDlpDownloadArguments(url, "/tmp/music with spaces.%(ext)s");
        Assert.Contains("--dump-json", arguments);
        Assert.Contains("--no-simulate", arguments);
        Assert.Contains("--no-playlist", arguments);
        Assert.Contains("--ignore-config", arguments);
        Assert.DoesNotContain("--skip-download", arguments);
        Assert.Equal("--", arguments[^2]);
        Assert.Equal(url, arguments[^1]);
        Assert.Contains("/tmp/music with spaces.%(ext)s", arguments);
    }

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(respond(request));
    }
}
