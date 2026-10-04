using System.Net.WebSockets;
using System.Text.Json;
using System.Threading.Channels;
using Spectralis.Core.SharedPlay;
using Spectralis.Core.StreamerQueue;
using Xunit;

namespace Spectralis.Tests.Core;

public sealed class StreamerQueueRealtimeClientTests
{
    private sealed class FakeTransport : ISharedPlaySocketTransport
    {
        private readonly Channel<string> _inbound = Channel.CreateUnbounded<string>();
        public readonly List<string> Sent = [];
        public readonly TaskCompletionSource Connected = new();
        public Uri? ConnectedTo;

        public WebSocketState State { get; private set; } = WebSocketState.None;

        public Task ConnectAsync(Uri uri, CancellationToken ct)
        {
            ConnectedTo = uri;
            State = WebSocketState.Open;
            Connected.TrySetResult();
            return Task.CompletedTask;
        }

        public Task SendTextAsync(string text, CancellationToken ct)
        {
            lock (Sent) Sent.Add(text);
            return Task.CompletedTask;
        }

        public async Task<string?> ReceiveTextAsync(CancellationToken ct)
        {
            try { return await _inbound.Reader.ReadAsync(ct); }
            catch (OperationCanceledException) { return null; }
            catch (ChannelClosedException) { return null; }
        }

        public void Inject(string frame) => _inbound.Writer.TryWrite(frame);
        public void CloseFromServer() => _inbound.Writer.TryComplete();
        public Task CloseAsync(CancellationToken ct) { State = WebSocketState.Closed; return Task.CompletedTask; }
        public void Dispose() { }
    }

    private static (StreamerQueueRealtimeClient client, FakeTransport transport, Func<int> connects) Create(int debounceMs = 20)
    {
        var transport = new FakeTransport();
        var connects = 0;
        var client = new StreamerQueueRealtimeClient(
            new Uri("wss://example.test/streamer-queue/v2/rooms/r1/socket"),
            () => { Interlocked.Increment(ref connects); return transport; },
            TimeSpan.FromMilliseconds(debounceMs));
        return (client, transport, () => Volatile.Read(ref connects));
    }

    [Theory]
    [InlineData("https://api.example.com", "wss://api.example.com/streamer-queue/v2/rooms/abc/socket")]
    [InlineData("https://api.example.com:8443/", "wss://api.example.com:8443/streamer-queue/v2/rooms/abc/socket")]
    [InlineData("http://localhost:3000", "ws://localhost:3000/streamer-queue/v2/rooms/abc/socket")]
    [InlineData("http://example.com", "wss://example.com/streamer-queue/v2/rooms/abc/socket")]
    public void Socket_uri_maps_the_scheme_and_keeps_plain_ws_for_loopback_only(string baseUrl, string expected)
    {
        Assert.Equal(expected, StreamerQueueRealtimeClient.BuildSocketUri(new Uri(baseUrl), "abc").ToString());
    }

    [Fact]
    public void Room_ids_are_escaped_in_the_path()
    {
        var uri = StreamerQueueRealtimeClient.BuildSocketUri(new Uri("https://api.test"), "a b/c");

        Assert.Contains("/rooms/a%20b%2Fc/socket", uri.AbsoluteUri);
    }

    [Fact]
    public async Task Hello_announces_protocol_2_as_the_app_and_asks_for_webhook_frames()
    {
        var (client, transport, _) = Create();
        client.Start();
        await transport.Connected.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await Task.Delay(50);

        string hello;
        lock (transport.Sent) hello = transport.Sent[0];
        using var doc = JsonDocument.Parse(hello);
        var root = doc.RootElement;

        Assert.Equal("hello", root.GetProperty("t").GetString());
        Assert.Equal(2, root.GetProperty("proto").GetInt32());
        Assert.Equal("app", root.GetProperty("client").GetProperty("kind").GetString());
        Assert.Equal("sq.webhooks", root.GetProperty("features")[0].GetString());
        await client.StopAsync();
    }

    [Fact]
    public async Task A_burst_of_change_frames_becomes_one_event_and_later_bursts_fire_again()
    {
        var (client, transport, _) = Create(debounceMs: 40);
        var changed = 0;
        client.Changed += () => Interlocked.Increment(ref changed);
        client.Start();
        await transport.Connected.Task.WaitAsync(TimeSpan.FromSeconds(2));

        for (var i = 0; i < 5; i++) transport.Inject("""{"v":2,"t":"sq.changed","status":{}}""");
        await Task.Delay(200);
        Assert.Equal(1, Volatile.Read(ref changed));

        transport.Inject("""{"v":2,"t":"sq.changed","status":{}}""");
        await Task.Delay(200);
        Assert.Equal(2, Volatile.Read(ref changed));
        await client.StopAsync();
    }

    [Fact]
    public async Task Webhook_submissions_are_reported_with_source_and_name()
    {
        var (client, transport, _) = Create();
        (string, string)? seen = null;
        client.WebhookSubmission += (source, name) => seen = (source, name);
        client.Start();
        await transport.Connected.Task.WaitAsync(TimeSpan.FromSeconds(2));

        transport.Inject("""{"v":2,"t":"sq.submission","source":"twitch","displayName":"Ann","submissionId":"s","status":"queued"}""");
        await Task.Delay(80);

        Assert.Equal(("twitch", "Ann"), seen);
        await client.StopAsync();
    }

    [Fact]
    public async Task Unrelated_and_malformed_frames_are_ignored()
    {
        var (client, transport, _) = Create();
        var changed = 0;
        client.Changed += () => changed++;
        client.Start();
        await transport.Connected.Task.WaitAsync(TimeSpan.FromSeconds(2));

        transport.Inject("""{"v":2,"t":"welcome","status":{}}""");
        transport.Inject("""{"v":2,"t":"pong"}""");
        transport.Inject("not json at all");
        transport.Inject("""{"v":2,"t":"error","code":"room_full","message":"x"}""");
        await Task.Delay(120);

        Assert.Equal(0, changed);
        Assert.False(client.UpdateRequired);
        await client.StopAsync();
    }

    [Fact]
    public async Task Update_required_is_reported_once_and_the_client_never_reconnects()
    {
        var (client, transport, connects) = Create();
        string? message = null;
        var raised = 0;
        client.UpdateRequiredReceived += m => { message = m; raised++; };
        client.Start();
        await transport.Connected.Task.WaitAsync(TimeSpan.FromSeconds(2));

        var frame = """{"v":2,"t":"error","code":"update_required","message":"Update Spectralis."}""";
        transport.Inject(frame);
        transport.Inject(frame);
        transport.CloseFromServer();
        await Task.Delay(1400); // longer than the first backoff step

        Assert.True(client.UpdateRequired);
        Assert.Equal("Update Spectralis.", message);
        Assert.Equal(1, raised);
        Assert.Equal(1, connects());
        await client.StopAsync();
    }

    [Fact]
    public async Task A_dropped_connection_reconnects_and_says_hello_again()
    {
        var (client, transport, connects) = Create();
        client.Start();
        await transport.Connected.Task.WaitAsync(TimeSpan.FromSeconds(2));

        transport.CloseFromServer();
        await Task.Delay(1500);

        Assert.True(connects() >= 2);
        await client.StopAsync();
    }

    [Fact]
    public async Task Stop_ends_the_loop_and_reports_disconnected()
    {
        var (client, transport, _) = Create();
        var states = new List<bool>();
        client.ConnectionChanged += states.Add;
        client.Start();
        await transport.Connected.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await Task.Delay(50);

        await client.StopAsync();

        Assert.False(client.IsConnected);
        Assert.Equal([true, false], states);
    }
}
