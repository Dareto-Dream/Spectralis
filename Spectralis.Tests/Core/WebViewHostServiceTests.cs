using Spectralis.Core.Integrations.Web;
using Spectralis.Core.Platform;
using Spectralis.Core.Visualizers;
using Xunit;

namespace Spectralis.Tests.Core;

/// <summary>In-memory IWebViewHost recording scripts and replaying messages.</summary>
public sealed class FakeWebViewHost : IWebViewHost
{
    public List<string> ExecutedScripts { get; } = new();
    public List<(string Hostname, string Folder)> VirtualHosts { get; } = new();
    public Uri? LastNavigation { get; private set; }
    public string? LastHtml { get; private set; }

    public event EventHandler<string>? MessageReceived;
    public event EventHandler? NavigationCompleted;

    public void MapVirtualHost(string hostname, string folderPath) => VirtualHosts.Add((hostname, folderPath));
    public void Navigate(Uri url) => LastNavigation = url;
    public void NavigateToString(string html) => LastHtml = html;

    public Task ExecuteScriptAsync(string script)
    {
        ExecutedScripts.Add(script);
        return Task.CompletedTask;
    }

    public void SimulateMessage(string json) => MessageReceived?.Invoke(this, json);
    public void SimulateNavigationCompleted() => NavigationCompleted?.Invoke(this, EventArgs.Empty);

    public void Dispose() { }
}

public sealed class WebViewHostServiceTests : IDisposable
{
    private readonly FakeWebViewHost _host = new();
    private readonly WebViewHostService _service;

    public WebViewHostServiceTests() => _service = new WebViewHostService(_host);

    public void Dispose() => _service.Dispose();

    [Fact]
    public void PlayTrack_DispatchesWithPosition()
    {
        AlbumTrackPlayRequest? request = null;
        _service.PlayTrackRequested += (_, e) => request = e;

        _host.SimulateMessage("""{"type":"spectral.playTrack","trackId":"t1","positionSeconds":12.5}""");

        Assert.NotNull(request);
        Assert.Equal("t1", request!.TrackId);
        Assert.Equal(12.5, request.PositionSeconds);
    }

    [Fact]
    public void PlayTrack_NegativeOrNonFinitePositionClampsToZero()
    {
        AlbumTrackPlayRequest? request = null;
        _service.PlayTrackRequested += (_, e) => request = e;

        _host.SimulateMessage("""{"type":"spectral.playTrack","trackId":"t1","positionSeconds":-5}""");

        Assert.Equal(0, request!.PositionSeconds);
    }

    [Fact]
    public void PauseResumeSeekExit_Dispatch()
    {
        var log = new List<string>();
        _service.PauseRequested += (_, _) => log.Add("pause");
        _service.ResumeRequested += (_, _) => log.Add("resume");
        _service.SeekRequested += (_, pos) => log.Add($"seek:{pos}");
        _service.ExitWorldRequested += (_, _) => log.Add("exit");

        _host.SimulateMessage("""{"type":"spectral.pause"}""");
        _host.SimulateMessage("""{"type":"spectral.resume"}""");
        _host.SimulateMessage("""{"type":"spectral.seek","positionSeconds":42}""");
        _host.SimulateMessage("""{"type":"spectral.exitWorld"}""");

        Assert.Equal(new[] { "pause", "resume", "seek:42", "exit" }, log);
    }

    [Fact]
    public void SaveBookmark_DispatchesAndCapsLabel()
    {
        AlbumBookmarkRequest? bookmark = null;
        _service.SaveBookmarkRequested += (_, e) => bookmark = e;

        var longLabel = new string('x', 1000);
        _host.SimulateMessage($$"""{"type":"spectral.saveBookmark","trackId":"t2","positionSeconds":7,"label":"{{longLabel}}"}""");

        Assert.NotNull(bookmark);
        Assert.Equal("t2", bookmark!.TrackId);
        Assert.Equal(256, bookmark.Label.Length);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("[1,2,3]")]
    [InlineData("""{"noType":true}""")]
    [InlineData("""{"type":"spectral.unknownAction"}""")]
    [InlineData("""{"type":"spectral.playTrack"}""")]
    [InlineData("""{"type":"spectral.seek","positionSeconds":"NaNish"}""")]
    public void InvalidOrUnknownMessages_AreDropped(string json)
    {
        var fired = 0;
        _service.PlayTrackRequested += (_, _) => fired++;
        _service.SeekRequested += (_, _) => fired++;
        _service.PauseRequested += (_, _) => fired++;

        _host.SimulateMessage(json);

        Assert.Equal(0, fired);
    }

    [Fact]
    public void OversizedMessage_IsDropped()
    {
        var fired = 0;
        _service.PauseRequested += (_, _) => fired++;

        var padding = new string(' ', 70 * 1024);
        _host.SimulateMessage($$"""{"type":"spectral.pause"{{padding}}}""");

        Assert.Equal(0, fired);
    }

    [Fact]
    public async Task PushFrame_EmitsGuardedSpectralCallback()
    {
        var frame = new VisualizerFrame(new float[64], new float[256], 0.5f, 0.3f);

        await _service.PushFrameAsync(frame, playing: true, position: 12f, currentTrackId: "t1");

        var script = Assert.Single(_host.ExecutedScripts);
        Assert.Contains("window.spectral?.onPlaybackFrame", script);
        Assert.Contains("\"levels\":", script);
        Assert.Contains("\"trackId\":\"t1\"", script);
    }

    [Fact]
    public async Task Bootstrap_DefinesAllSpectralCallbacks()
    {
        await _service.InjectBootstrapAsync();

        var script = Assert.Single(_host.ExecutedScripts);
        foreach (var callback in new[] { "onReady", "onTrackChanged", "onPlaybackFrame", "onTrackCompleted", "onSessionRestored" })
        {
            Assert.Contains(callback, script);
        }
    }
}

public class ContentSecurityPolicyTests
{
    [Fact]
    public void Policy_DefaultDeniesNetworkAndEmbedding()
    {
        var policy = WebViewHostService.BuildContentSecurityPolicy(allowNetworkAccess: false);

        Assert.Contains("connect-src 'none'", policy);
        Assert.Contains("object-src 'none'", policy);
        Assert.Contains("frame-src 'none'", policy);
        Assert.Contains("default-src 'self'", policy);
    }

    [Fact]
    public void Policy_NetworkCapabilityOpensHttpsConnectOnly()
    {
        var policy = WebViewHostService.BuildContentSecurityPolicy(allowNetworkAccess: true);

        Assert.Contains("connect-src https:", policy);
        Assert.DoesNotContain("connect-src 'none'", policy);
    }

    [Fact]
    public void Inject_PlacesMetaAtHeadStart()
    {
        var html = "<html><head><title>x</title></head><body></body></html>";

        var result = WebViewHostService.InjectContentSecurityPolicy(html, false);

        var cspIndex = result.IndexOf("Content-Security-Policy", StringComparison.Ordinal);
        var titleIndex = result.IndexOf("<title>", StringComparison.Ordinal);
        Assert.True(cspIndex > 0 && cspIndex < titleIndex, "CSP must precede capsule markup.");
    }

    [Fact]
    public void Inject_CreatesHeadWhenMissing()
    {
        Assert.Contains("Content-Security-Policy", WebViewHostService.InjectContentSecurityPolicy("<html><body>x</body></html>", false));
        Assert.Contains("Content-Security-Policy", WebViewHostService.InjectContentSecurityPolicy("just text", false));
    }
}
