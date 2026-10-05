using System.Net;
using Spectralis.Core.Diagnostics;
using Xunit;

namespace Spectralis.Tests.Core;

public sealed class NetworkLogTests
{
    private sealed class Fake(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(respond(request));
    }

    private static HttpClient Client(NetworkLogStore store, Func<HttpRequestMessage, HttpResponseMessage> respond) =>
        new(new NetworkLogHandler("test", new Fake(respond), store));

    [Fact]
    public async Task RecordsStatusTimingAndSizes()
    {
        var store = new NetworkLogStore();
        using var http = Client(store, _ => new HttpResponseMessage(HttpStatusCode.NotFound)
        {
            Content = new StringContent("nope"),
        });

        await http.GetAsync("https://api.example.com/rooms?limit=5");

        var e = Assert.Single(store.Snapshot());
        Assert.Equal(NetworkKind.Http, e.Kind);
        Assert.Equal("GET", e.Method);
        Assert.Equal("api.example.com", e.Host);
        Assert.Equal(404, e.StatusCode);
        Assert.Equal(NetworkState.Complete, e.State);
        Assert.True(e.IsError);
        Assert.Equal(4, e.ResponseBytes);
        Assert.NotNull(e.DurationMs);
        Assert.Equal("test", e.Source);
    }

    [Fact]
    public async Task MasksSecretsInUrlsAndHeaders()
    {
        var store = new NetworkLogStore();
        using var http = Client(store, _ => new HttpResponseMessage(HttpStatusCode.OK));
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://user:pw@api.example.com/x?token=abc123&page=2&API_KEY=zzz");
        request.Headers.TryAddWithoutValidation("Authorization", "Bearer sekrit");
        request.Headers.TryAddWithoutValidation("X-Session-Key", "hostkey");
        request.Headers.TryAddWithoutValidation("Accept", "application/json");

        await http.SendAsync(request);

        var e = Assert.Single(store.Snapshot());
        Assert.DoesNotContain("abc123", e.Url);
        Assert.DoesNotContain("zzz", e.Url);
        Assert.DoesNotContain("pw", e.Url.Replace("page", string.Empty));
        Assert.Contains("page=2", e.Url);
        var headers = e.RequestHeaders.ToDictionary(h => h.Key, h => h.Value, StringComparer.OrdinalIgnoreCase);
        Assert.Equal("Bearer ***", headers["Authorization"]);
        Assert.Equal("***", headers["X-Session-Key"]);
        Assert.Equal("application/json", headers["Accept"]);
        Assert.DoesNotContain("sekrit", string.Join('|', e.RequestHeaders.Select(h => h.Value)));
    }

    [Fact]
    public async Task RecordsFailuresAndStillThrows()
    {
        var store = new NetworkLogStore();
        using var http = Client(store, _ => throw new HttpRequestException("connection refused"));

        await Assert.ThrowsAsync<HttpRequestException>(() => http.GetAsync("https://down.example.com/"));

        var e = Assert.Single(store.Snapshot());
        Assert.Equal(NetworkState.Failed, e.State);
        Assert.Contains("connection refused", e.Error);
        Assert.True(e.IsError);
    }

    [Fact]
    public async Task DisabledLogRecordsNothingAndPassesThrough()
    {
        var store = new NetworkLogStore { Enabled = false };
        using var http = Client(store, _ => new HttpResponseMessage(HttpStatusCode.OK));

        var response = await http.GetAsync("https://api.example.com/");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty(store.Snapshot());
    }

    [Fact]
    public void StoreKeepsOnlyTheNewestEntries()
    {
        var store = new NetworkLogStore(3);
        for (var i = 1; i <= 5; i++) store.Begin(new NetworkEntry { Url = $"https://x/{i}" });

        var urls = store.Snapshot().Select(e => e.Url).ToArray();
        Assert.Equal(["https://x/3", "https://x/4", "https://x/5"], urls);
    }

    [Fact]
    public void UpdateOfAnEvictedEntryIsIgnored()
    {
        var store = new NetworkLogStore(1);
        var first = store.Begin(new NetworkEntry { Url = "https://x/1" });
        store.Begin(new NetworkEntry { Url = "https://x/2" });

        store.Update(first, e => e with { StatusCode = 200 });

        Assert.Null(Assert.Single(store.Snapshot()).StatusCode);
    }

    [Fact]
    public async Task TrackedOperationsRecordSuccessAndFailure()
    {
        var store = new NetworkLogStore();

        var value = await NetworkLog.TrackAsync("updater", "CHECK", "https://cdn.example.com/releases.json?sig=1", () => Task.FromResult(7), store);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            NetworkLog.TrackAsync("updater", "DOWNLOAD", "https://cdn.example.com/a.nupkg", (Func<Task>)(() => throw new InvalidOperationException("disk full")), store));

        Assert.Equal(7, value);
        var all = store.Snapshot();
        Assert.Equal(NetworkState.Complete, all[0].State);
        Assert.DoesNotContain("sig=1", all[0].Url);
        Assert.Equal(NetworkState.Failed, all[1].State);
        Assert.Contains("disk full", all[1].Error);
        Assert.All(all, e => Assert.Equal(NetworkKind.Operation, e.Kind));
    }

    [Fact]
    public void SocketTraceCountsFramesAndEndsOnce()
    {
        var store = new NetworkLogStore();
        var trace = NetworkLog.OpenSocket("shared-play", new Uri("wss://api.example.com/rooms/ABC?key=hostkey"), store);

        Assert.Equal(NetworkState.Pending, store.Snapshot()[0].State);
        trace.Opened();
        trace.Sent(10);
        trace.Sent(5);
        trace.Received(100);
        Assert.Equal(NetworkState.Live, store.Snapshot()[0].State);
        trace.Closed("bye");
        trace.Failed(new Exception("late error that must not overwrite the close"));

        var e = Assert.Single(store.Snapshot());
        Assert.Equal(NetworkState.Complete, e.State);
        Assert.Equal(2, e.FramesSent);
        Assert.Equal(15, e.BytesSent);
        Assert.Equal(1, e.FramesReceived);
        Assert.Equal(100, e.BytesReceived);
        Assert.Equal("bye", e.Reason);
        Assert.DoesNotContain("hostkey", e.Url);
        Assert.Null(e.Error);
    }

    [Fact]
    public async Task ChangedFiresOnBeginUpdateAndClear()
    {
        var store = new NetworkLogStore();
        var count = 0;
        store.Changed += () => Interlocked.Increment(ref count);
        using var http = Client(store, _ => new HttpResponseMessage(HttpStatusCode.OK));

        await http.GetAsync("https://api.example.com/");
        store.Clear();

        Assert.Equal(3, count);
    }
}
