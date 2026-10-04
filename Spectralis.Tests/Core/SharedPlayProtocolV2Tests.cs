using System.Net.WebSockets;
using System.Text.Json;
using System.Threading.Channels;
using Spectralis.Core.SharedPlay;
using Xunit;

namespace Spectralis.Tests.Core;

public sealed class SharedPlayProtocolV2Tests
{
    private sealed class FakeTransport : ISharedPlaySocketTransport
    {
        private readonly Channel<string> _inbound = Channel.CreateUnbounded<string>();
        public readonly List<string> Sent = [];
        public readonly TaskCompletionSource Connected = new();

        public WebSocketState State { get; private set; } = WebSocketState.None;

        public Task ConnectAsync(Uri uri, CancellationToken ct)
        {
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

    private static (SharedPlayRoomSocket socket, FakeTransport transport, Func<int> connects) Create()
    {
        var transport = new FakeTransport();
        var connects = 0;
        var socket = new SharedPlayRoomSocket(
            new Uri("wss://example.test/shared-play/v2/sessions/ABC123/socket"),
            "listener", "client-1", "Tester",
            transportFactory: () => { Interlocked.Increment(ref connects); return transport; });
        return (socket, transport, () => Volatile.Read(ref connects));
    }

    [Fact]
    public async Task Hello_announces_protocol_client_and_features()
    {
        var (socket, transport, _) = Create();
        socket.Start();
        await transport.Connected.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await Task.Delay(50);

        string hello;
        lock (transport.Sent) hello = transport.Sent[0];
        using var doc = JsonDocument.Parse(hello);
        var root = doc.RootElement;

        Assert.Equal(SharedPlayDefaults.RealtimeProtocolVersion, root.GetProperty("proto").GetInt32());
        Assert.Equal(SharedPlayDefaults.RealtimeProtocolVersion, root.GetProperty("v").GetInt32());
        Assert.Equal("app", root.GetProperty("client").GetProperty("kind").GetString());
        Assert.False(string.IsNullOrWhiteSpace(root.GetProperty("client").GetProperty("version").GetString()));
        var features = root.GetProperty("features").EnumerateArray().Select(f => f.GetString()).ToArray();
        Assert.Contains("roster", features);
        Assert.DoesNotContain("queue.v2", features);
        await socket.StopAsync();
    }

    [Fact]
    public async Task Welcome_records_the_features_the_server_agreed_to()
    {
        var (socket, transport, _) = Create();
        socket.Start();
        await transport.Connected.Task.WaitAsync(TimeSpan.FromSeconds(2));

        transport.Inject("""
        {"v":2,"t":"welcome","proto":2,"features":["roster","commands"],
         "you":{"clientId":"client-1","role":"follower"},"caps":{},"roster":[]}
        """);
        await Task.Delay(80);

        Assert.Equal(["roster", "commands"], socket.NegotiatedFeatures);
        await socket.StopAsync();
    }

    [Fact]
    public async Task Update_required_is_surfaced_once_and_the_socket_does_not_reconnect()
    {
        var (socket, transport, connects) = Create();
        string? message = null;
        var raised = 0;
        socket.UpdateRequiredReceived += m => { message = m; raised++; };
        socket.Start();
        await transport.Connected.Task.WaitAsync(TimeSpan.FromSeconds(2));

        var frame = """{"v":2,"t":"error","code":"update_required","message":"Update Spectralis to join.","minProto":2}""";
        transport.Inject(frame);
        transport.Inject(frame);
        transport.CloseFromServer();
        // Longer than the first backoff step (500ms): a reconnecting client would have connected again.
        await Task.Delay(900);

        Assert.True(socket.UpdateRequired);
        Assert.Equal("Update Spectralis to join.", message);
        Assert.Equal(1, raised);
        Assert.Equal(1, connects());
        Assert.False(socket.IsConnected);
        await socket.StopAsync();
    }

    [Fact]
    public async Task Ordinary_errors_still_reconnect_and_are_not_update_required()
    {
        var (socket, transport, connects) = Create();
        socket.UpdateRequiredReceived += _ => Assert.Fail("not an update error");
        socket.Start();
        await transport.Connected.Task.WaitAsync(TimeSpan.FromSeconds(2));

        transport.Inject("""{"v":2,"t":"error","code":"room_full","message":"Room is full"}""");
        transport.CloseFromServer();
        await Task.Delay(900);

        Assert.False(socket.UpdateRequired);
        Assert.True(connects() >= 2);
        await socket.StopAsync();
    }
}
