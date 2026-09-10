using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace Spectralis.Core.SharedPlay;

/// <summary>Transport seam for <see cref="SharedPlayRoomSocket"/> so tests can feed
/// frames without a real network. The default implementation wraps
/// <see cref="ClientWebSocket"/>.</summary>
public interface ISharedPlaySocketTransport : IDisposable
{
    WebSocketState State { get; }
    Task ConnectAsync(Uri uri, CancellationToken ct);
    Task SendTextAsync(string text, CancellationToken ct);
    /// <summary>Returns the next text message, or null when the socket closes.</summary>
    Task<string?> ReceiveTextAsync(CancellationToken ct);
    Task CloseAsync(CancellationToken ct);
}

internal sealed class ClientWebSocketTransport : ISharedPlaySocketTransport
{
    private ClientWebSocket _ws = new();
    private readonly byte[] _buffer = new byte[64 * 1024];

    public WebSocketState State => _ws.State;

    public async Task ConnectAsync(Uri uri, CancellationToken ct)
    {
        _ws.Dispose();
        _ws = new ClientWebSocket();
        _ws.Options.KeepAliveInterval = TimeSpan.FromSeconds(20);
        await _ws.ConnectAsync(uri, ct).ConfigureAwait(false);
    }

    public Task SendTextAsync(string text, CancellationToken ct) =>
        _ws.SendAsync(Encoding.UTF8.GetBytes(text), WebSocketMessageType.Text, endOfMessage: true, ct);

    public async Task<string?> ReceiveTextAsync(CancellationToken ct)
    {
        using var ms = new MemoryStream();
        while (true)
        {
            WebSocketReceiveResult result;
            try
            {
                result = await _ws.ReceiveAsync(_buffer, ct).ConfigureAwait(false);
            }
            catch (WebSocketException)
            {
                return null;
            }
            catch (OperationCanceledException)
            {
                return null;
            }

            if (result.MessageType == WebSocketMessageType.Close)
                return null;

            ms.Write(_buffer, 0, result.Count);
            if (result.EndOfMessage)
                return Encoding.UTF8.GetString(ms.ToArray());
        }
    }

    public async Task CloseAsync(CancellationToken ct)
    {
        try
        {
            if (_ws.State == WebSocketState.Open)
                await _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "bye", ct).ConfigureAwait(false);
        }
        catch { /* ignore */ }
    }

    public void Dispose() => _ws.Dispose();
}

/// <summary>Client for a collaborative Shared Play room. Connects as host (with the
/// session key) or listener, keeps the connection alive with exponential-backoff
/// reconnect, and surfaces room events. Events fire on a background thread — the
/// App layer marshals to the UI thread.</summary>
public sealed class SharedPlayRoomSocket : IDisposable
{
    private static readonly TimeSpan[] Backoff =
    {
        TimeSpan.FromMilliseconds(500), TimeSpan.FromSeconds(1),
        TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(15),
    };

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly Uri _uri;
    private readonly string _role;      // "host" | "listener"
    private readonly string _clientId;
    private readonly string _name;
    private readonly string? _sessionKey;
    private readonly Func<ISharedPlaySocketTransport> _transportFactory;
    private readonly SemaphoreSlim _sendGate = new(1, 1);

    private ISharedPlaySocketTransport? _transport;
    private CancellationTokenSource? _cts;
    private Task? _loop;
    private volatile bool _connected;

    private string? _lastStateJson;
    private string? _lastQueueJson;

    public SharedPlayRoomSocket(
        Uri socketUri,
        string role,
        string clientId,
        string name,
        string? sessionKey = null,
        Func<ISharedPlaySocketTransport>? transportFactory = null)
    {
        _uri = socketUri;
        _role = role;
        _clientId = clientId;
        _name = name;
        _sessionKey = sessionKey;
        _transportFactory = transportFactory ?? (() => new ClientWebSocketTransport());
    }

    public bool IsConnected => _connected;
    public string ClientId => _clientId;

    public event Action<JsonElement>? StateReceived;
    public event Action<JsonElement>? QueueReceived;
    public event Action<IReadOnlyList<SharedPlayMember>>? RosterReceived;
    public event Action<SharedPlayCapabilities>? CapsReceived;
    public event Action<string, string>? ReactionReceived;
    public event Action<SharedPlaySkipProgress>? SkipProgressReceived;
    public event Action<SharedPlayIncomingCommand>? CommandReceived;
    public event Action? Kicked;
    public event Action<string, string>? ErrorReceived;
    public event Action<bool>? ConnectionChanged;
    public event Action<JsonElement, SharedPlayCapabilities, IReadOnlyList<SharedPlayMember>>? Welcomed;

    public void Start()
    {
        if (_loop is not null) return;
        _cts = new CancellationTokenSource();
        _loop = Task.Run(() => RunAsync(_cts.Token));
    }

    public async Task StopAsync()
    {
        _cts?.Cancel();
        if (_transport is not null)
        {
            try { await _transport.CloseAsync(CancellationToken.None).ConfigureAwait(false); }
            catch { /* ignore */ }
        }
        if (_loop is not null)
        {
            try { await _loop.ConfigureAwait(false); }
            catch { /* ignore */ }
        }
        _loop = null;
        SetConnected(false);
    }

    private async Task RunAsync(CancellationToken ct)
    {
        var attempt = 0;
        while (!ct.IsCancellationRequested)
        {
            try
            {
                _transport?.Dispose();
                _transport = _transportFactory();
                await _transport.ConnectAsync(_uri, ct).ConfigureAwait(false);

                await SendRawAsync(HelloFrame(), ct).ConfigureAwait(false);
                if (_role == "host")
                {
                    if (_lastStateJson is not null)
                        await SendRawAsync(_lastStateJson, ct).ConfigureAwait(false);
                    if (_lastQueueJson is not null)
                        await SendRawAsync(_lastQueueJson, ct).ConfigureAwait(false);
                }

                attempt = 0;
                SetConnected(true);

                while (!ct.IsCancellationRequested)
                {
                    var text = await _transport.ReceiveTextAsync(ct).ConfigureAwait(false);
                    if (text is null) break;
                    Dispatch(text);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception)
            {
                // fall through to backoff
            }

            SetConnected(false);
            if (ct.IsCancellationRequested) break;
            var delay = Backoff[Math.Min(attempt, Backoff.Length - 1)];
            attempt++;
            try { await Task.Delay(delay, ct).ConfigureAwait(false); }
            catch (OperationCanceledException) { break; }
        }
        SetConnected(false);
    }

    private void SetConnected(bool value)
    {
        if (_connected == value) return;
        _connected = value;
        try { ConnectionChanged?.Invoke(value); } catch { /* handler threw */ }
    }

    private void Dispatch(string text)
    {
        JsonDocument doc;
        try { doc = JsonDocument.Parse(text); }
        catch { return; }

        using (doc)
        {
            var root = doc.RootElement;
            var t = root.TryGetProperty("t", out var tEl) ? tEl.GetString() : null;
            switch (t)
            {
                case "welcome":
                    var caps = root.TryGetProperty("caps", out var cEl)
                        ? SharedPlayCapabilities.FromJson(cEl)
                        : SharedPlayCapabilities.Default;
                    var roster = ReadRoster(root);
                    var you = root.TryGetProperty("you", out var yEl) ? yEl.Clone() : default;
                    Raise(() => Welcomed?.Invoke(you, caps, roster));
                    Raise(() => CapsReceived?.Invoke(caps));
                    Raise(() => RosterReceived?.Invoke(roster));
                    if (root.TryGetProperty("state", out var ws) && ws.ValueKind == JsonValueKind.Object)
                    { var c = ws.Clone(); Raise(() => StateReceived?.Invoke(c)); }
                    if (root.TryGetProperty("queue", out var wq) && wq.ValueKind == JsonValueKind.Object)
                    { var c = wq.Clone(); Raise(() => QueueReceived?.Invoke(c)); }
                    break;

                case "state":
                    if (root.TryGetProperty("playback", out var pb))
                    { var c = pb.Clone(); Raise(() => StateReceived?.Invoke(c)); }
                    break;

                case "queue":
                    if (root.TryGetProperty("queue", out var q))
                    { var c = q.Clone(); Raise(() => QueueReceived?.Invoke(c)); }
                    break;

                case "roster":
                    var r = ReadRoster(root);
                    Raise(() => RosterReceived?.Invoke(r));
                    break;

                case "caps":
                    if (root.TryGetProperty("caps", out var caps2El))
                    {
                        var parsed = SharedPlayCapabilities.FromJson(caps2El);
                        Raise(() => CapsReceived?.Invoke(parsed));
                    }
                    break;

                case "reaction":
                    var kind = root.TryGetProperty("kind", out var kEl) ? kEl.GetString() ?? "spark" : "spark";
                    var by = root.TryGetProperty("by", out var bEl) ? bEl.GetString() ?? "" : "";
                    Raise(() => ReactionReceived?.Invoke(kind, by));
                    break;

                case "skip":
                    var votes = root.TryGetProperty("votes", out var vEl) && vEl.TryGetInt32(out var vN) ? vN : 0;
                    var req = root.TryGetProperty("required", out var rEl) && rEl.TryGetInt32(out var rN) ? rN : 0;
                    Raise(() => SkipProgressReceived?.Invoke(new SharedPlaySkipProgress(votes, req)));
                    break;

                case "command":
                    var cmd = root.TryGetProperty("cmd", out var cmdEl) ? cmdEl.GetString() ?? "" : "";
                    var payload = root.TryGetProperty("payload", out var plEl) ? plEl.Clone() : default;
                    var byName = "";
                    var byId = "";
                    if (root.TryGetProperty("by", out var byEl) && byEl.ValueKind == JsonValueKind.Object)
                    {
                        byName = byEl.TryGetProperty("name", out var bn) ? bn.GetString() ?? "" : "";
                        byId = byEl.TryGetProperty("clientId", out var bi) ? bi.GetString() ?? "" : "";
                    }
                    Raise(() => CommandReceived?.Invoke(new SharedPlayIncomingCommand(cmd, byName, byId, payload)));
                    break;

                case "kicked":
                    Raise(() => Kicked?.Invoke());
                    break;

                case "error":
                    var ec = root.TryGetProperty("code", out var ecEl) ? ecEl.GetString() ?? "error" : "error";
                    var em = root.TryGetProperty("message", out var emEl) ? emEl.GetString() ?? "" : "";
                    Raise(() => ErrorReceived?.Invoke(ec, em));
                    break;
            }
        }
    }

    private static IReadOnlyList<SharedPlayMember> ReadRoster(JsonElement root)
    {
        var list = new List<SharedPlayMember>();
        // `roster` events carry the array as `members`; `welcome` carries it as `roster`.
        JsonElement arr = default;
        var found = (root.TryGetProperty("members", out arr) && arr.ValueKind == JsonValueKind.Array)
                    || (root.TryGetProperty("roster", out arr) && arr.ValueKind == JsonValueKind.Array);
        if (found)
        {
            foreach (var m in arr.EnumerateArray())
            {
                var id = m.TryGetProperty("clientId", out var i) ? i.GetString() ?? "" : "";
                var name = m.TryGetProperty("name", out var n) ? n.GetString() ?? "Listener" : "Listener";
                var role = SharedPlayRoleExtensions.ParseRole(m.TryGetProperty("role", out var ro) ? ro.GetString() : null);
                var isHost = m.TryGetProperty("isHost", out var h) && h.ValueKind == JsonValueKind.True;
                if (id.Length > 0) list.Add(new SharedPlayMember(id, name, role, isHost));
            }
        }
        return list;
    }

    private static void Raise(Action action)
    {
        try { action(); } catch { /* a handler threw; keep the loop alive */ }
    }

    // ── Senders ───────────────────────────────────────────────────────────────

    public void PublishState(object playback)
    {
        var frame = Envelope("state.publish", ("playback", playback));
        _lastStateJson = frame;
        _ = SendAsync(frame);
    }

    public void PublishQueue(object queue)
    {
        var frame = Envelope("queue.publish", ("queue", queue));
        _lastQueueJson = frame;
        _ = SendAsync(frame);
    }

    public void SetCaps(SharedPlayCapabilities caps) =>
        _ = SendAsync(Envelope("caps.set", ("caps", new
        {
            queueAdd = caps.QueueAdd,
            queueRemove = caps.QueueRemove,
            queueReorder = caps.QueueReorder,
            transport = caps.Transport,
            voteSkip = caps.VoteSkip,
            skipVotesRequired = caps.SkipVotesRequired,
        })));

    public void SetMemberRole(string clientId, SharedPlayRole role) =>
        _ = SendAsync(Envelope("member.role", ("clientId", clientId), ("role", role.ToWire())));

    public void KickMember(string clientId) =>
        _ = SendAsync(Envelope("member.kick", ("clientId", clientId)));

    public void SendTransport(string action, double? position = null) =>
        _ = SendAsync(position is { } p
            ? Envelope("transport", ("action", action), ("position", p))
            : Envelope("transport", ("action", action)));

    public void RequestQueueAdd(string url, string? title = null, string? artist = null, string? sourceKind = null) =>
        _ = SendAsync(Envelope("queue.add", ("item", new
        {
            url,
            title,
            artist,
            sourceKind = sourceKind ?? "url",
        })));

    public void RemoveQueueItem(string id) =>
        _ = SendAsync(Envelope("queue.remove", ("id", id)));

    public void MoveQueueItem(string id, int toIndex) =>
        _ = SendAsync(Envelope("queue.move", ("id", id), ("toIndex", toIndex)));

    public void VoteSkip() => _ = SendAsync(Envelope("skip.vote"));

    public void React(string kind) => _ = SendAsync(Envelope("reaction", ("kind", kind)));

    public void Ping() => _ = SendAsync(Envelope("ping"));

    private string HelloFrame()
    {
        var dict = new Dictionary<string, object?>
        {
            ["v"] = SharedPlayDefaults.SocketEnvelopeVersion,
            ["t"] = "hello",
            ["role"] = _role,
            ["clientId"] = _clientId,
            ["name"] = _name,
        };
        if (_role == "host" && _sessionKey is not null) dict["key"] = _sessionKey;
        return JsonSerializer.Serialize(dict, Json);
    }

    private static string Envelope(string t, params (string Key, object? Value)[] fields)
    {
        var dict = new Dictionary<string, object?>
        {
            ["v"] = SharedPlayDefaults.SocketEnvelopeVersion,
            ["t"] = t,
        };
        foreach (var (k, val) in fields) dict[k] = val;
        return JsonSerializer.Serialize(dict, Json);
    }

    private async Task SendAsync(string frame)
    {
        if (!_connected || _transport is null) return;
        try { await SendRawAsync(frame, _cts?.Token ?? CancellationToken.None).ConfigureAwait(false); }
        catch { /* dropped; host state/queue are re-published on reconnect */ }
    }

    private async Task SendRawAsync(string frame, CancellationToken ct)
    {
        if (_transport is null) return;
        await _sendGate.WaitAsync(ct).ConfigureAwait(false);
        try { await _transport.SendTextAsync(frame, ct).ConfigureAwait(false); }
        finally { _sendGate.Release(); }
    }

    public void Dispose()
    {
        _cts?.Cancel();
        _transport?.Dispose();
        _cts?.Dispose();
        _sendGate.Dispose();
    }
}
