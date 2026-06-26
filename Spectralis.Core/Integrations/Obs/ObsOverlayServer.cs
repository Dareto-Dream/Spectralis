using System.Net;
using System.Text;
using System.Text.Json;

namespace Spectralis.Core.Integrations.Obs;

public sealed class ObsOverlayServer : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private readonly HttpListener listener = new();
    private readonly string token;
    private readonly string baseUrl;
    private readonly CancellationTokenSource cts = new();
    private readonly object stateLock = new();
    private readonly List<SseClient> sseClients = [];

    // Injected delegates for layout/banner lookups
    private readonly Func<string?, ObsLayout>? getLayout;
    private readonly Func<string, byte[]?> getBannerHtml;

    private ObsOverlayState currentState = ObsOverlayState.Empty;
    private byte[]? artworkBytes;
    private string artworkContentType = "image/jpeg";
    private bool started;
    private bool disposed;
    private int layoutSeq;

    public ObsOverlayServer(
        int port,
        string token,
        Func<string?, ObsLayout>? getLayout = null,
        Func<string, byte[]?>? getBannerHtml = null)
    {
        this.token = token;
        this.getLayout = getLayout;
        this.getBannerHtml = getBannerHtml ?? (_ => null);
        baseUrl = $"http://127.0.0.1:{port}/obs/{token}";
        listener.Prefixes.Add($"http://127.0.0.1:{port}/obs/");
    }

    public string BaseUrl => baseUrl;

    public string GetOverlayUrl(string overlayId) =>
        string.IsNullOrWhiteSpace(overlayId)
            ? baseUrl
            : $"{baseUrl}/o/{Uri.EscapeDataString(overlayId.Trim())}";

    public int CurrentLayoutSeq { get { lock (stateLock) return layoutSeq; } }

    public void BumpLayoutVersion() { lock (stateLock) layoutSeq++; }

    public void Start()
    {
        if (started || disposed) return;
        started = true;
        listener.Start();
        _ = AcceptLoopAsync(cts.Token);
    }

    public void UpdateState(ObsOverlayState state, byte[]? artwork, string artworkContentType = "image/jpeg", bool artworkChanged = false)
    {
        string json;
        lock (stateLock)
        {
            currentState = state;
            if (artworkChanged)
            {
                artworkBytes = artwork;
                if (artwork is not null)
                    this.artworkContentType = artworkContentType;
            }

            json = JsonSerializer.Serialize(state, JsonOptions);
        }

        BroadcastSse(json);
    }

    private async Task AcceptLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var ctx = await listener.GetContextAsync().WaitAsync(ct);
                _ = Task.Run(() => HandleRequestAsync(ctx, ct), ct);
            }
            catch (OperationCanceledException) { break; }
            catch { }
        }
    }

    private async Task HandleRequestAsync(HttpListenerContext ctx, CancellationToken ct)
    {
        try
        {
            var path = ctx.Request.Url?.AbsolutePath ?? "";
            if (!TryMatchRoute(path, out var overlayId, out var resource))
            {
                Respond(ctx, 404, "text/plain", "Not found");
                return;
            }

            ctx.Response.Headers["Access-Control-Allow-Origin"] = "*";

            // Route: visualizer-banner/{vizId}
            if (resource.StartsWith("visualizer-banner/", StringComparison.OrdinalIgnoreCase))
            {
                ServeVisualizerBanner(ctx, resource["visualizer-banner/".Length..]);
                return;
            }

            switch (resource)
            {
                case "":
                    ServeHtml(ctx, overlayId);
                    break;
                case "state":
                    ServeState(ctx);
                    break;
                case "events":
                    await SseAsync(ctx, ct);
                    break;
                case "assets/artwork":
                    ServeArtwork(ctx);
                    break;
                case "visualizer":
                    ServeVisualizer(ctx);
                    break;
                case "layout":
                    ServeLayout(ctx, overlayId);
                    break;
                default:
                    Respond(ctx, 404, "text/plain", "Not found");
                    break;
            }
        }
        catch { try { ctx.Response.Close(); } catch { } }
    }

    private bool TryMatchRoute(string path, out string? overlayId, out string resource)
    {
        overlayId = null;
        resource = "";
        var prefix = $"/obs/{token}";
        if (!path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return false;

        var rest = path[prefix.Length..].TrimStart('/');
        if (rest.StartsWith("o/", StringComparison.OrdinalIgnoreCase))
        {
            var slash = rest.IndexOf('/', 2);
            var encodedOverlayId = slash < 0 ? rest[2..] : rest[2..slash];
            overlayId = Uri.UnescapeDataString(encodedOverlayId);
            resource = slash < 0 ? "" : rest[(slash + 1)..];
            return !string.IsNullOrWhiteSpace(overlayId);
        }

        resource = rest;
        return true;
    }

    private void ServeHtml(HttpListenerContext ctx, string? overlayId)
    {
        var html = ObsOverlayHtml.Template.Replace("{BASE}", GetOverlayUrl(overlayId ?? ""));
        var bytes = Encoding.UTF8.GetBytes(html);
        ctx.Response.ContentType = "text/html; charset=utf-8";
        ctx.Response.ContentLength64 = bytes.Length;
        ctx.Response.OutputStream.Write(bytes);
        ctx.Response.Close();
    }

    private void ServeState(HttpListenerContext ctx)
    {
        string json;
        lock (stateLock) json = JsonSerializer.Serialize(currentState, JsonOptions);
        var bytes = Encoding.UTF8.GetBytes(json);
        ctx.Response.ContentType = "application/json; charset=utf-8";
        ctx.Response.Headers["Cache-Control"] = "no-cache";
        ctx.Response.ContentLength64 = bytes.Length;
        ctx.Response.OutputStream.Write(bytes);
        ctx.Response.Close();
    }

    private void ServeLayout(HttpListenerContext ctx, string? overlayId)
    {
        var layout = getLayout?.Invoke(overlayId) ?? ObsLayout.CreateDefault();
        var json = layout.ToJson();
        var bytes = Encoding.UTF8.GetBytes(json);
        ctx.Response.ContentType = "application/json; charset=utf-8";
        ctx.Response.Headers["Cache-Control"] = "no-cache";
        ctx.Response.ContentLength64 = bytes.Length;
        ctx.Response.OutputStream.Write(bytes);
        ctx.Response.Close();
    }

    private void ServeVisualizerBanner(HttpListenerContext ctx, string vizId)
    {
        if (string.IsNullOrWhiteSpace(vizId))
        {
            Respond(ctx, 400, "text/plain", "Missing vizId");
            return;
        }

        var bannerBytes = getBannerHtml(Uri.UnescapeDataString(vizId));
        if (bannerBytes is null)
        {
            Respond(ctx, 404, "text/plain", "No banner for this visualizer");
            return;
        }

        ctx.Response.ContentType = "text/html; charset=utf-8";
        ctx.Response.Headers["Cache-Control"] = "no-cache";
        ctx.Response.ContentLength64 = bannerBytes.Length;
        ctx.Response.OutputStream.Write(bannerBytes);
        ctx.Response.Close();
    }

    private void ServeVisualizer(HttpListenerContext ctx)
    {
        ObsVisualizerState viz;
        lock (stateLock) viz = currentState.Visualizer;
        var json = JsonSerializer.Serialize(viz, JsonOptions);
        var bytes = Encoding.UTF8.GetBytes(json);
        ctx.Response.ContentType = "application/json; charset=utf-8";
        ctx.Response.Headers["Cache-Control"] = "no-cache";
        ctx.Response.ContentLength64 = bytes.Length;
        ctx.Response.OutputStream.Write(bytes);
        ctx.Response.Close();
    }

    private void ServeArtwork(HttpListenerContext ctx)
    {
        byte[]? data;
        string ct;
        lock (stateLock) { data = artworkBytes; ct = artworkContentType; }

        if (data is null)
        {
            Respond(ctx, 404, "text/plain", "No artwork");
            return;
        }

        ctx.Response.ContentType = ct;
        ctx.Response.Headers["Cache-Control"] = "no-store";
        ctx.Response.ContentLength64 = data.Length;
        ctx.Response.OutputStream.Write(data);
        ctx.Response.Close();
    }

    private async Task SseAsync(HttpListenerContext ctx, CancellationToken serverCt)
    {
        ctx.Response.ContentType = "text/event-stream";
        ctx.Response.Headers["Cache-Control"] = "no-cache";
        ctx.Response.Headers["X-Accel-Buffering"] = "no";
        ctx.Response.SendChunked = true;

        var writer = new StreamWriter(ctx.Response.OutputStream, Encoding.UTF8, leaveOpen: true) { AutoFlush = true };
        var client = new SseClient(writer);

        lock (stateLock)
        {
            sseClients.Add(client);
            var initial = JsonSerializer.Serialize(currentState, JsonOptions);
            _ = client.TrySendAsync($"data:{initial}\n\n");
        }

        try
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(serverCt, client.Ct);
            while (!linked.Token.IsCancellationRequested)
                await Task.Delay(30_000, linked.Token);
        }
        catch (OperationCanceledException) { }
        finally
        {
            lock (stateLock) sseClients.Remove(client);
            client.Dispose();
            try { ctx.Response.Close(); } catch { }
        }
    }

    private void BroadcastSse(string json)
    {
        var msg = $"data:{json}\n\n";
        List<SseClient> snapshot;
        lock (stateLock) snapshot = [..sseClients];

        foreach (var client in snapshot)
            _ = client.TrySendAsync(msg);
    }

    private static void Respond(HttpListenerContext ctx, int status, string contentType, string body)
    {
        var bytes = Encoding.UTF8.GetBytes(body);
        ctx.Response.StatusCode = status;
        ctx.Response.ContentType = contentType;
        ctx.Response.ContentLength64 = bytes.Length;
        try { ctx.Response.OutputStream.Write(bytes); } catch { }
        try { ctx.Response.Close(); } catch { }
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        cts.Cancel();
        cts.Dispose();
        try { if (listener.IsListening) listener.Stop(); } catch { }
        listener.Close();
    }

    private sealed class SseClient(StreamWriter writer) : IDisposable
    {
        private readonly CancellationTokenSource cts = new();

        public CancellationToken Ct => cts.Token;

        public async Task TrySendAsync(string message)
        {
            try { await writer.WriteAsync(message); }
            catch { cts.Cancel(); }
        }

        public void Dispose()
        {
            cts.Cancel();
            cts.Dispose();
            try { writer.Dispose(); } catch { }
        }
    }
}
