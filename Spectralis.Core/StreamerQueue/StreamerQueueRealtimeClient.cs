using System.Text.Json;
using Spectralis.Core.SharedPlay;

namespace Spectralis.Core.StreamerQueue;

/// <summary>
/// Listens to a Streamer Queue room's realtime socket (docs/realtime-protocol.md) so the app can refresh the
/// moment something changes instead of waiting for the next poll. The socket only carries change
/// notifications, never private data, so a "changed" is simply a cue to fetch over REST as the owner.
/// Polling stays as the safety net: this client never has to be right, just fast.
/// Events fire on a background thread; the App layer marshals to the UI thread.
/// </summary>
public sealed class StreamerQueueRealtimeClient : IDisposable
{
    private static readonly TimeSpan[] Backoff =
    {
        TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5),
        TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(30),
    };

    private readonly Uri _uri;
    private readonly Func<ISharedPlaySocketTransport> _transportFactory;
    private readonly TimeSpan _debounce;
    private readonly object _gate = new();

    private ISharedPlaySocketTransport? _transport;
    private CancellationTokenSource? _cts;
    private Task? _loop;
    private Timer? _debounceTimer;
    private volatile bool _connected;
    private volatile bool _updateRequired;

    public StreamerQueueRealtimeClient(
        Uri socketUri,
        Func<ISharedPlaySocketTransport>? transportFactory = null,
        TimeSpan? debounce = null)
    {
        _uri = socketUri;
        _transportFactory = transportFactory ?? (() => new ClientWebSocketTransport());
        _debounce = debounce ?? TimeSpan.FromMilliseconds(400);
    }

    /// <summary>Wire path for a room's socket: /streamer-queue/v2/rooms/{id}/socket, https→wss.</summary>
    public static Uri BuildSocketUri(Uri baseUri, string roomId)
    {
        var path = $"/streamer-queue/v2/rooms/{Uri.EscapeDataString(roomId.Trim())}/socket";
        var http = new Uri(baseUri, path);
        var loopback = http.IsLoopback || http.Host == "localhost";
        var builder = new UriBuilder(http)
        {
            Scheme = http.Scheme == Uri.UriSchemeHttp && loopback ? "ws" : "wss",
        };
        if (builder.Scheme == "wss") builder.Port = http.IsDefaultPort ? -1 : http.Port;
        return builder.Uri;
    }

    public bool IsConnected => _connected;

    /// <summary>True once the server said this build is too old; the client stops for good.</summary>
    public bool UpdateRequired => _updateRequired;

    /// <summary>The room changed (queue, now playing, open/closed). A burst collapses into one event.</summary>
    public event Action? Changed;

    /// <summary>A chat-bot or webhook submission was accepted: (source, displayName).</summary>
    public event Action<string, string>? WebhookSubmission;

    /// <summary>The server refused this client as too old; the argument is a user-presentable message.</summary>
    public event Action<string>? UpdateRequiredReceived;

    public event Action<bool>? ConnectionChanged;

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
        lock (_gate)
        {
            _debounceTimer?.Dispose();
            _debounceTimer = null;
        }
        SetConnected(false);
    }

    public void Dispose()
    {
        _ = StopAsync();
        _transport?.Dispose();
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
                await _transport.SendTextAsync(HelloFrame(), ct).ConfigureAwait(false);
                attempt = 0;
                SetConnected(true);

                while (!ct.IsCancellationRequested)
                {
                    var text = await _transport.ReceiveTextAsync(ct).ConfigureAwait(false);
                    if (text is null) break;
                    Dispatch(text);
                    if (_updateRequired) break;
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
            if (ct.IsCancellationRequested || _updateRequired) break;
            var delay = Backoff[Math.Min(attempt++, Backoff.Length - 1)];
            try { await Task.Delay(delay, ct).ConfigureAwait(false); }
            catch (OperationCanceledException) { break; }
        }
        SetConnected(false);
    }

    private static string HelloFrame() => JsonSerializer.Serialize(new Dictionary<string, object?>
    {
        ["v"] = SharedPlayDefaults.RealtimeProtocolVersion,
        ["t"] = "hello",
        ["proto"] = SharedPlayDefaults.RealtimeProtocolVersion,
        ["client"] = new Dictionary<string, object?>
        {
            ["kind"] = "app",
            ["version"] = typeof(StreamerQueueRealtimeClient).Assembly.GetName().Version?.ToString(3) ?? "0.0.0",
        },
        // The owner's dashboard wants to see chat-bot submissions arrive.
        ["features"] = new[] { "sq.webhooks" },
    });

    private void Dispatch(string text)
    {
        JsonDocument doc;
        try { doc = JsonDocument.Parse(text); }
        catch { return; }

        using (doc)
        {
            var root = doc.RootElement;
            var type = root.TryGetProperty("t", out var t) && t.ValueKind == JsonValueKind.String ? t.GetString() : null;
            switch (type)
            {
                case "sq.changed":
                    ScheduleChanged();
                    break;

                case "sq.submission":
                    var source = Text(root, "source");
                    var name = Text(root, "displayName");
                    try { WebhookSubmission?.Invoke(source, name); } catch { /* handler threw */ }
                    break;

                case "error" when Text(root, "code") == "update_required" && !_updateRequired:
                    _updateRequired = true;
                    var message = Text(root, "message");
                    try { UpdateRequiredReceived?.Invoke(message.Length > 0 ? message : "Update Spectralis to keep the queue live."); }
                    catch { /* handler threw */ }
                    break;
            }
        }
    }

    // A burst of writes (approve, reorder, now-playing) becomes a single refresh.
    private void ScheduleChanged()
    {
        lock (_gate)
        {
            if (_debounceTimer is not null) return;
            _debounceTimer = new Timer(_ =>
            {
                lock (_gate)
                {
                    _debounceTimer?.Dispose();
                    _debounceTimer = null;
                }
                try { Changed?.Invoke(); } catch { /* handler threw */ }
            }, null, _debounce, Timeout.InfiniteTimeSpan);
        }
    }

    private static string Text(JsonElement root, string name) =>
        root.TryGetProperty(name, out var el) && el.ValueKind == JsonValueKind.String ? el.GetString() ?? "" : "";

    private void SetConnected(bool value)
    {
        if (_connected == value) return;
        _connected = value;
        try { ConnectionChanged?.Invoke(value); } catch { /* handler threw */ }
    }
}
