using System.Net;
using System.Text.Json;
using Spectralis.App.Services;
using Spectralis.Core.Integrations.Spotify;
using Spectralis.Core.Platform;
using Xunit;

namespace Spectralis.Tests.App;

public class SpotifyLinkPlaybackTests
{
    private const string Id = "6eLyGKZI33EAgaRyWJ6FvH";

    [Theory]
    [InlineData("https://open.spotify.com/album/6eLyGKZI33EAgaRyWJ6FvH?si=r1WYlagcRA-TvgatdMLuIQ&utm_source=copy-link", "album", true)]
    [InlineData("https://open.spotify.com/track/6eLyGKZI33EAgaRyWJ6FvH?si=share", "track", false)]
    [InlineData("https://open.spotify.com/intl-de/playlist/6eLyGKZI33EAgaRyWJ6FvH", "playlist", true)]
    [InlineData("spotify:artist:6eLyGKZI33EAgaRyWJ6FvH", "artist", true)]
    [InlineData("open.spotify.com/episode/6eLyGKZI33EAgaRyWJ6FvH", "episode", false)]
    public async Task Resolve_ReturnsSdkPlaybackWithoutWidgetOrDownload(string input, string kind, bool context)
    {
        var result = await new OpenUrlService().ResolveAsync(input, CancellationToken.None);
        Assert.Equal(RemoteAudioServiceKind.Spotify, result.Kind);
        Assert.Equal($"spotify:{kind}:{Id}", result.ExternalId);
        Assert.Equal($"https://open.spotify.com/{kind}/{Id}", result.SourceUrl);
        Assert.False(result.IsWebViewFallback());
        Assert.False(result.MetadataOnly);
        Assert.Empty(result.AudioUrl);
        Assert.Null(result.CachedAudioPath);
        Assert.True(SpotifyPlaybackLink.TryParse(input, out var link));
        Assert.Equal(context, link.IsContext);
        Assert.True(OpenUrlService.MightBeUrl(input));
    }

    [Theory]
    [InlineData("https://open.spotify.com.evil.test/track/6eLyGKZI33EAgaRyWJ6FvH")]
    [InlineData("https://open.spotify.com/track/bad")]
    [InlineData("spotify:track:6eLyGKZI33EAgaRyWJ6FvH:extra")]
    [InlineData("https://open.spotify.com/show/6eLyGKZI33EAgaRyWJ6FvH")]
    public void Parse_RejectsInvalidPlaybackTargets(string input) =>
        Assert.False(SpotifyPlaybackLink.TryParse(input, out _));

    [Theory]
    [InlineData("album", "context_uri")]
    [InlineData("track", "uris")]
    public async Task PlayLink_ActivatesTransfersThenRequestsExactTarget(string kind, string bodyProperty)
    {
        var requests = new Requests();
        using var http = new HttpClient(requests);
        using var spotify = Service(http);
        var host = new Host();
        var player = new SpotifyPlaybackHostService(host, spotify, () => "client-id");
        host.Send("{\"type\":\"ready\",\"deviceId\":\"sdk-device\"}");

        Assert.True(await player.PlayLinkAsync($"spotify:{kind}:{Id}"));
        Assert.Equal(2, host.Scripts.Count);
        Assert.Contains("spotifyActivate", host.Scripts[0]);
        Assert.Contains("spotifyResume", host.Scripts[1]);
        Assert.Equal(2, requests.Calls.Count);
        Assert.Equal("/v1/me/player", requests.Calls[0].Path);
        using var transfer = JsonDocument.Parse(requests.Calls[0].Body);
        Assert.False(transfer.RootElement.GetProperty("play").GetBoolean());
        Assert.Equal("sdk-device", transfer.RootElement.GetProperty("device_ids")[0].GetString());
        Assert.Equal("/v1/me/player/play?device_id=sdk-device", requests.Calls[1].Path);
        using var play = JsonDocument.Parse(requests.Calls[1].Body);
        var target = play.RootElement.GetProperty(bodyProperty);
        Assert.Equal($"spotify:{kind}:{Id}", kind == "track" ? target[0].GetString() : target.GetString());
        Assert.All(requests.Calls, call => Assert.Equal(HttpMethod.Put, call.Method));
    }

    [Fact]
    public async Task PlayLink_TransferFailureNeverRequestsTrack()
    {
        var requests = new Requests { Status = HttpStatusCode.Forbidden };
        using var http = new HttpClient(requests);
        using var spotify = Service(http);
        var host = new Host();
        var player = new SpotifyPlaybackHostService(host, spotify, () => "client-id");
        host.Send("{\"type\":\"ready\",\"deviceId\":\"sdk-device\"}");
        Assert.False(await player.PlayLinkAsync($"spotify:album:{Id}"));
        Assert.Single(requests.Calls);
        Assert.DoesNotContain(host.Scripts, script => script.Contains("spotifyResume"));
        Assert.Contains("transfer failed", player.StatusMessage);
    }

    [Fact]
    public async Task PlayLink_MissingConfigurationNeverRequestsPlayback()
    {
        var requests = new Requests();
        using var http = new HttpClient(requests);
        using var spotify = Service(http);
        var player = new SpotifyPlaybackHostService(new Host(), spotify, () => "");
        Assert.False(await player.PlayLinkAsync($"spotify:album:{Id}"));
        Assert.Contains("client ID", player.StatusMessage);
        Assert.Empty(requests.Calls);
    }

    [Fact]
    public async Task PlayLink_CancelledOpenNeverRequestsPlayback()
    {
        var requests = new Requests();
        using var http = new HttpClient(requests);
        using var spotify = Service(http);
        var host = new Host();
        var player = new SpotifyPlaybackHostService(host, spotify, () => "client-id");
        host.Send("{\"type\":\"ready\",\"deviceId\":\"sdk-device\"}");
        using var cancel = new CancellationTokenSource();
        cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => player.PlayLinkAsync($"spotify:album:{Id}", cancel.Token));
        Assert.Empty(requests.Calls);
    }

    private static SpotifyService Service(HttpClient http) => new(new SpotifyTokenStore
    {
        AccessToken = "test-access", RefreshToken = "test-refresh", ExpiresAt = DateTime.UtcNow.AddHours(1)
    }, http);

    private sealed class Requests : HttpMessageHandler
    {
        public HttpStatusCode Status { get; init; } = HttpStatusCode.NoContent;
        public List<(HttpMethod Method, string Path, string Body)> Calls { get; } = [];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls.Add((request.Method, request.RequestUri!.PathAndQuery, await request.Content!.ReadAsStringAsync(cancellationToken)));
            return new HttpResponseMessage(Status) { Content = new StringContent("") };
        }
    }

    private sealed class Host : IWebViewHost
    {
        public List<string> Scripts { get; } = [];
        public event EventHandler<string>? MessageReceived;
        public event EventHandler? NavigationCompleted { add { } remove { } }
        public event EventHandler? NavigationFailed { add { } remove { } }
        public void Send(string message) => MessageReceived?.Invoke(this, message);
        public Task ExecuteScriptAsync(string script) { Scripts.Add(script); return Task.CompletedTask; }
        public void MapVirtualHost(string hostname, string folderPath) { }
        public void Navigate(Uri url) { }
        public void NavigateToString(string html) { }
        public void NudgeResize() { }
        public void Dispose() { }
    }
}
