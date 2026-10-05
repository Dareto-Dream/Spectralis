using System.Diagnostics;

namespace Spectralis.Core.Diagnostics;

/// <summary>Records each request that passes through it in the network log, then gets out of the way.</summary>
public sealed class NetworkLogHandler : DelegatingHandler
{
    private readonly string _source;
    private readonly NetworkLogStore _store;

    public NetworkLogHandler(string source, HttpMessageHandler? inner = null, NetworkLogStore? store = null)
        : base(inner ?? new HttpClientHandler())
    {
        _source = source;
        _store = store ?? NetworkLog.Default;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (!_store.Enabled) return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);

        var uri = request.RequestUri;
        var id = _store.Begin(new NetworkEntry
        {
            Kind = NetworkKind.Http,
            State = NetworkState.Pending,
            Source = _source,
            Method = request.Method.Method,
            Url = LogRedaction.Url(uri),
            Host = uri?.Host ?? string.Empty,
            RequestBytes = request.Content?.Headers.ContentLength,
            RequestHeaders = NetworkLog.HeadersOf(request.Headers, request.Content?.Headers),
        });
        var clock = Stopwatch.StartNew();
        try
        {
            var response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
            var ms = clock.Elapsed.TotalMilliseconds;
            _store.Update(id, e => e with
            {
                State = NetworkState.Complete,
                DurationMs = ms,
                StatusCode = (int)response.StatusCode,
                Reason = response.ReasonPhrase,
                ResponseBytes = response.Content.Headers.ContentLength,
                ContentType = response.Content.Headers.ContentType?.ToString(),
                ResponseHeaders = NetworkLog.HeadersOf(response.Headers, response.Content.Headers),
            });
            return response;
        }
        catch (OperationCanceledException)
        {
            var ms = clock.Elapsed.TotalMilliseconds;
            _store.Update(id, e => e with { State = NetworkState.Failed, DurationMs = ms, Error = cancellationToken.IsCancellationRequested ? "canceled" : "timed out" });
            throw;
        }
        catch (Exception ex)
        {
            var ms = clock.Elapsed.TotalMilliseconds;
            _store.Update(id, e => e with { State = NetworkState.Failed, DurationMs = ms, Error = NetworkLog.Describe(ex) });
            throw;
        }
    }
}

/// <summary>
/// The one place the app builds its HTTP clients, so every call lands in the network log. Pass the part of the
/// app making the calls as <paramref name="source"/>; pass a configured handler when a client needs one.
/// </summary>
public static class NetworkClients
{
    public static HttpClient Create(string source, TimeSpan? timeout = null, HttpMessageHandler? handler = null)
    {
        var client = new HttpClient(new NetworkLogHandler(source, handler));
        if (timeout is { } t) client.Timeout = t;
        return client;
    }
}
