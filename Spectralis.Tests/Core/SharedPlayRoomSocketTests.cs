using System.Net.WebSockets;
using System.Text.Json;
using System.Threading.Channels;
using Spectralis.Core.SharedPlay;
using Xunit;

namespace Spectralis.Tests.Core;

public sealed class SharedPlayCapabilitiesTests
{
    [Fact]
    public void Default_HasExpectedShape()
    {
        var d = SharedPlayCapabilities.Default;
        Assert.Equal("everyone", d.QueueAdd);
        Assert.Equal("host", d.Transport);
        Assert.True(d.VoteSkip);
        Assert.Equal(3, d.SkipVotesRequired);
    }

    [Fact]
    public void FromJson_ParsesAndClampsVotes()
    {
        using var doc = JsonDocument.Parse(
            """{"queueAdd":"codj","transport":"everyone","voteSkip":false,"skipVotesRequired":99}""");
        var caps = SharedPlayCapabilities.FromJson(doc.RootElement);
        Assert.Equal("codj", caps.QueueAdd);
        Assert.Equal("everyone", caps.Transport);
        Assert.False(caps.VoteSkip);
        Assert.Equal(20, caps.SkipVotesRequired);
    }

    [Fact]
    public void FromJson_MissingFields_FallBackToDefault()
    {
        using var doc = JsonDocument.Parse("""{"transport":"codj"}""");
        var caps = SharedPlayCapabilities.FromJson(doc.RootElement);
        Assert.Equal("codj", caps.Transport);
        Assert.Equal("everyone", caps.QueueAdd);
        Assert.Equal(3, caps.SkipVotesRequired);
    }

    [Theory]
    [InlineData("host", SharedPlayRole.Host)]
    [InlineData("codj", SharedPlayRole.CoDj)]
    [InlineData("follower", SharedPlayRole.Follower)]
    [InlineData("weird", SharedPlayRole.Follower)]
    public void RoleRoundTrips(string wire, SharedPlayRole expected)
    {
        Assert.Equal(expected, SharedPlayRoleExtensions.ParseRole(wire));
    }
}

public sealed class SharedPlayRoomSocketTests
{
    /// <summary>A fake transport: outbound frames land in <see cref="Sent"/>,
    /// inbound frames are fed via <see cref="Inject"/>.</summary>
    private sealed class FakeTransport : ISharedPlaySocketTransport
    {
        private readonly Channel<string> _inbound = Channel.CreateUnbounded<string>();
        public readonly List<string> Sent = new();
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
        }

        public void Inject(string frame) => _inbound.Writer.TryWrite(frame);
        public Task CloseAsync(CancellationToken ct) { State = WebSocketState.Closed; return Task.CompletedTask; }
        public void Dispose() { }
    }

    private static async Task<(SharedPlayRoomSocket socket, FakeTransport transport)> ConnectAsync(string role = "listener")
    {
        var transport = new FakeTransport();
        var socket = new SharedPlayRoomSocket(
            new Uri("wss://example.test/shared-play/v2/sessions/ABC123/socket"),
            role, "client-1", "Tester", sessionKey: role == "host" ? "key" : null,
            transportFactory: () => transport);
        socket.Start();
        await transport.Connected.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await Task.Delay(50);
        return (socket, transport);
    }

    [Fact]
    public async Task SendsHelloOnConnect()
    {
        var (socket, transport) = await ConnectAsync("host");
        string hello;
        lock (transport.Sent) hello = transport.Sent[0];
        using var doc = JsonDocument.Parse(hello);
        Assert.Equal("hello", doc.RootElement.GetProperty("t").GetString());
        Assert.Equal("host", doc.RootElement.GetProperty("role").GetString());
        Assert.Equal("key", doc.RootElement.GetProperty("key").GetString());
        await socket.StopAsync();
    }

    [Fact]
    public async Task WelcomeFrame_RaisesCapsAndRoster()
    {
        var (socket, transport) = await ConnectAsync();
        SharedPlayCapabilities? caps = null;
        IReadOnlyList<SharedPlayMember>? roster = null;
        socket.CapsReceived += c => caps = c;
        socket.RosterReceived += r => roster = r;

        transport.Inject("""
        {"v":1,"t":"welcome","you":{"clientId":"client-1","role":"follower"},
         "caps":{"transport":"everyone","skipVotesRequired":2},
         "roster":[
            {"clientId":"h","name":"Host","role":"host","isHost":true},
            {"clientId":"client-1","name":"Tester","role":"follower"}]}
        """);
        await Task.Delay(80);

        Assert.NotNull(caps);
        Assert.Equal("everyone", caps!.Transport);
        Assert.NotNull(roster);
        Assert.Equal(2, roster!.Count);
        Assert.Contains(roster, m => m is { Name: "Host", IsHost: true });
        await socket.StopAsync();
    }

    [Fact]
    public async Task CommandFrame_RaisesTypedCommand()
    {
        var (socket, transport) = await ConnectAsync("host");
        SharedPlayIncomingCommand? cmd = null;
        socket.CommandReceived += c => cmd = c;

        transport.Inject("""
        {"v":1,"t":"command","cmd":"transport","payload":{"action":"pause"},
         "by":{"clientId":"lis","name":"Alice"}}
        """);
        await Task.Delay(80);

        Assert.NotNull(cmd);
        Assert.Equal("transport", cmd!.Cmd);
        Assert.Equal("Alice", cmd.ByName);
        Assert.Equal("pause", cmd.Payload.GetProperty("action").GetString());
        await socket.StopAsync();
    }

    [Fact]
    public async Task Senders_EmitCorrectEnvelopes()
    {
        var (socket, transport) = await ConnectAsync();

        socket.RequestQueueAdd("https://youtube.com/watch?v=x", "Song");
        socket.SendTransport("seek", 42.5);
        socket.VoteSkip();
        await Task.Delay(60);

        List<string> frames;
        lock (transport.Sent) frames = transport.Sent.ToList();

        var add = frames.Select(f => JsonDocument.Parse(f).RootElement)
            .First(e => e.GetProperty("t").GetString() == "queue.add");
        Assert.Equal("https://youtube.com/watch?v=x", add.GetProperty("item").GetProperty("url").GetString());

        var seek = frames.Select(f => JsonDocument.Parse(f).RootElement)
            .First(e => e.GetProperty("t").GetString() == "transport");
        Assert.Equal("seek", seek.GetProperty("action").GetString());
        Assert.Equal(42.5, seek.GetProperty("position").GetDouble());

        Assert.Contains(frames, f => JsonDocument.Parse(f).RootElement.GetProperty("t").GetString() == "skip.vote");
        await socket.StopAsync();
    }

    [Fact]
    public async Task KickedFrame_RaisesKicked()
    {
        var (socket, transport) = await ConnectAsync();
        var kicked = false;
        socket.Kicked += () => kicked = true;
        transport.Inject("""{"v":1,"t":"kicked"}""");
        await Task.Delay(80);
        Assert.True(kicked);
        await socket.StopAsync();
    }
}
