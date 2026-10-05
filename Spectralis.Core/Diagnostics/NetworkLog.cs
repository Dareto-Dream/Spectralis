using System.Diagnostics;
using System.Net.Http.Headers;

namespace Spectralis.Core.Diagnostics;

/// <summary>
/// The app's network log: every HTTP call made through <see cref="NetworkClients"/>, every websocket the app
/// opens, and the operations it tracks around code it can't see inside (the updater). The Network tab in
/// Developer Tools reads this. Bodies are never kept, and URLs and headers are masked first (see <see cref="LogRedaction"/>).
/// </summary>
public static class NetworkLog
{
    public static NetworkLogStore Default { get; } = new();

    public static event Action? Changed
    {
        add => Default.Changed += value;
        remove => Default.Changed -= value;
    }

    public static bool Enabled
    {
        get => Default.Enabled;
        set => Default.Enabled = value;
    }

    public static IReadOnlyList<NetworkEntry> Snapshot() => Default.Snapshot();
    public static void Clear() => Default.Clear();

    /// <summary>
    /// Runs <paramref name="action"/> and records it as one entry. For calls the app can't intercept, such as the
    /// updater's own downloads: it shows what was attempted, how long it took and whether it threw.
    /// </summary>
    public static async Task<T> TrackAsync<T>(string source, string method, string url, Func<Task<T>> action, NetworkLogStore? store = null)
    {
        store ??= Default;
        if (!store.Enabled) return await action().ConfigureAwait(false);

        var safeUrl = Uri.TryCreate(url, UriKind.Absolute, out var uri) ? LogRedaction.Url(uri) : url;
        var id = store.Begin(new NetworkEntry
        {
            Kind = NetworkKind.Operation, State = NetworkState.Pending, Source = source, Method = method,
            Url = safeUrl, Host = uri?.Host ?? string.Empty,
        });
        var clock = Stopwatch.StartNew();
        try
        {
            var result = await action().ConfigureAwait(false);
            store.Update(id, e => e with { State = NetworkState.Complete, DurationMs = clock.Elapsed.TotalMilliseconds, Reason = "ok" });
            return result;
        }
        catch (OperationCanceledException)
        {
            store.Update(id, e => e with { State = NetworkState.Failed, DurationMs = clock.Elapsed.TotalMilliseconds, Error = "canceled" });
            throw;
        }
        catch (Exception ex)
        {
            store.Update(id, e => e with { State = NetworkState.Failed, DurationMs = clock.Elapsed.TotalMilliseconds, Error = Describe(ex) });
            throw;
        }
    }

    public static async Task TrackAsync(string source, string method, string url, Func<Task> action, NetworkLogStore? store = null) =>
        await TrackAsync<object?>(source, method, url, async () => { await action().ConfigureAwait(false); return null; }, store).ConfigureAwait(false);

    /// <summary>Starts tracing a websocket. Call <see cref="SocketTrace.Opened"/> once it connects.</summary>
    public static SocketTrace OpenSocket(string source, Uri url, NetworkLogStore? store = null) => new(store ?? Default, source, url);

    internal static string Describe(Exception ex) => ex.InnerException is { } inner && ex is not HttpRequestException
        ? $"{ex.GetType().Name}: {ex.Message} ({inner.GetType().Name}: {inner.Message})"
        : $"{ex.GetType().Name}: {ex.Message}";

    internal static IReadOnlyList<KeyValuePair<string, string>> HeadersOf(HttpHeaders? headers, HttpContentHeaders? content)
    {
        var all = new List<KeyValuePair<string, IEnumerable<string>>>();
        if (headers is not null) all.AddRange(headers);
        if (content is not null) all.AddRange(content);
        return LogRedaction.Headers(all);
    }
}

/// <summary>One websocket as it connects, carries frames and closes. Counts and sizes only; frame contents are not kept.</summary>
public sealed class SocketTrace
{
    private readonly NetworkLogStore _store;
    private readonly long _id;
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private int _closed;

    internal SocketTrace(NetworkLogStore store, string source, Uri url)
    {
        _store = store;
        if (!store.Enabled) { _id = -1; return; }
        _id = store.Begin(new NetworkEntry
        {
            Kind = NetworkKind.WebSocket, State = NetworkState.Pending, Source = source, Method = "WS",
            Url = LogRedaction.Url(url), Host = url.Host,
        });
    }

    public void Opened() => Change(e => e with { State = NetworkState.Live, StatusCode = 101, Reason = "open" });

    public void Sent(long bytes) => Change(e => e with { FramesSent = e.FramesSent + 1, BytesSent = e.BytesSent + bytes });

    public void Received(long bytes) => Change(e => e with { FramesReceived = e.FramesReceived + 1, BytesReceived = e.BytesReceived + bytes });

    public void Closed(string? reason = null)
    {
        if (Interlocked.Exchange(ref _closed, 1) == 1) return;
        var ms = _clock.Elapsed.TotalMilliseconds;
        Change(e => e with { State = e.State == NetworkState.Failed ? e.State : NetworkState.Complete, DurationMs = ms, Reason = reason ?? "closed" });
    }

    public void Failed(Exception ex)
    {
        if (Interlocked.Exchange(ref _closed, 1) == 1) return;
        var ms = _clock.Elapsed.TotalMilliseconds;
        Change(e => e with { State = NetworkState.Failed, DurationMs = ms, Error = NetworkLog.Describe(ex) });
    }

    private void Change(Func<NetworkEntry, NetworkEntry> change)
    {
        if (_id < 0) return;
        _store.Update(_id, change);
    }
}
