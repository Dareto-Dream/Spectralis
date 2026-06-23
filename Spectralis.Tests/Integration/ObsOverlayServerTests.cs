using System.Net;
using System.Text.Json;
using Spectralis.Core.Integrations.Obs;
using Xunit;

namespace Spectralis.Tests.Integration;

public sealed class ObsOverlayServerTests : IDisposable
{
    private readonly ObsOverlayServer _server;
    private readonly HttpClient _client = new() { Timeout = TimeSpan.FromSeconds(10) };
    private readonly string _baseUrl;
    private const string Token = "test-token-1234";

    public ObsOverlayServerTests()
    {
        var port = GetFreePort();
        _server = new ObsOverlayServer(port, Token);
        _server.Start();
        _baseUrl = $"http://127.0.0.1:{port}/obs/{Token}";
    }

    public void Dispose()
    {
        _server.Dispose();
        _client.Dispose();
    }

    private static int GetFreePort()
    {
        var listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    [Fact]
    public async Task Root_ServesOverlayHtml()
    {
        var response = await _client.GetAsync(_baseUrl);
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("text/html", response.Content.Headers.ContentType!.ToString());
        Assert.Contains(_baseUrl, body); // {BASE} substituted
    }

    [Fact]
    public async Task WrongToken_Is404()
    {
        var response = await _client.GetAsync(_baseUrl.Replace(Token, "wrong-token"));
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task State_ReflectsUpdates()
    {
        _server.UpdateState(new ObsOverlayState
        {
            Track = new ObsTrackState { Title = "Test Song", Artist = "Tester" },
            Playback = new ObsPlaybackState { IsPlaying = true, PositionSeconds = 42 },
        }, artwork: null);

        var json = await _client.GetStringAsync($"{_baseUrl}/state");
        using var doc = JsonDocument.Parse(json);

        Assert.Equal("Test Song", doc.RootElement.GetProperty("track").GetProperty("title").GetString());
        Assert.True(doc.RootElement.GetProperty("playback").GetProperty("isPlaying").GetBoolean());
    }

    [Fact]
    public async Task Layout_ServesDefaultLayoutJson()
    {
        var json = await _client.GetStringAsync($"{_baseUrl}/layout");
        using var doc = JsonDocument.Parse(json);

        Assert.True(doc.RootElement.GetProperty("widgets").GetArrayLength() > 0);
    }

    [Fact]
    public async Task Artwork_404UntilProvided_ThenServedWithNoStore()
    {
        var missing = await _client.GetAsync($"{_baseUrl}/assets/artwork");
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);

        _server.UpdateState(ObsOverlayState.Empty, artwork: [1, 2, 3, 4], artworkContentType: "image/png");

        var response = await _client.GetAsync($"{_baseUrl}/assets/artwork");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("image/png", response.Content.Headers.ContentType!.MediaType);
        Assert.Equal(4, (await response.Content.ReadAsByteArrayAsync()).Length);
    }

    [Fact]
    public async Task Sse_PushesInitialAndUpdatedState()
    {
        using var stream = await _client.GetStreamAsync($"{_baseUrl}/events");
        using var reader = new StreamReader(stream);

        // Initial snapshot arrives on connect.
        var initial = await ReadSseDataAsync(reader);
        Assert.Contains("\"track\"", initial);

        _server.UpdateState(new ObsOverlayState
        {
            Track = new ObsTrackState { Title = "Pushed Track" },
        }, artwork: null);

        var pushed = await ReadSseDataAsync(reader);
        Assert.Contains("Pushed Track", pushed);
    }

    [Fact]
    public void BuiltInPresets_AllParseBackToLayouts()
    {
        Assert.Equal(10, BuiltInObsPresets.All.Count);
        Assert.All(BuiltInObsPresets.All, preset =>
        {
            Assert.NotNull(preset.Layout);
            Assert.NotEmpty(preset.Layout!.Widgets);
        });
    }

    private static async Task<string> ReadSseDataAsync(StreamReader reader)
    {
        var deadline = Task.Delay(TimeSpan.FromSeconds(10));
        while (true)
        {
            var lineTask = reader.ReadLineAsync();
            var winner = await Task.WhenAny(lineTask, deadline);
            Assert.NotSame(deadline, winner);

            var line = await lineTask;
            if (line is null)
            {
                throw new InvalidOperationException("SSE stream closed unexpectedly.");
            }

            if (line.StartsWith("data:", StringComparison.Ordinal))
            {
                return line["data:".Length..];
            }
        }
    }
}
