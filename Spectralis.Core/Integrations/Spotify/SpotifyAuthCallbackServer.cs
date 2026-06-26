using System.Net;
using System.Text;

namespace Spectralis.Core.Integrations.Spotify;

public sealed class SpotifyAuthCallbackServer : IDisposable
{
    private const string ListenPrefix = "http://127.0.0.1:5127/callback/";
    public const string RedirectUri = "http://127.0.0.1:5127/callback";

    private readonly HttpListener listener = new();
    private bool disposed;

    public SpotifyAuthCallbackServer()
    {
        listener.Prefixes.Add(ListenPrefix);
    }

    public async Task<(string Code, string State)?> WaitForCallbackAsync(CancellationToken cancellationToken)
    {
        listener.Start();
        try
        {
            var contextTask = listener.GetContextAsync();
            await Task.WhenAny(contextTask, Task.Delay(Timeout.Infinite, cancellationToken));

            if (cancellationToken.IsCancellationRequested || !contextTask.IsCompletedSuccessfully)
                return null;

            var context = contextTask.Result;
            var query = context.Request.QueryString;
            var code = query["code"];
            var state = query["state"];

            const string html = """
                <!DOCTYPE html>
                <html>
                <head><title>Spectralis — Spotify Linked</title></head>
                <body style="font-family:'Segoe UI',sans-serif;text-align:center;padding:60px;background:#111;color:#eee">
                <h2 style="color:#1db954">Spectralis linked to Spotify!</h2>
                <p>You can close this window and return to Spectralis.</p>
                </body>
                </html>
                """;

            var bytes = Encoding.UTF8.GetBytes(html);
            context.Response.ContentType = "text/html; charset=utf-8";
            context.Response.ContentLength64 = bytes.Length;
            await context.Response.OutputStream.WriteAsync(bytes, CancellationToken.None);
            context.Response.Close();

            return code is not null && state is not null ? (code, state) : null;
        }
        finally
        {
            try { listener.Stop(); } catch { }
        }
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        try { if (listener.IsListening) listener.Stop(); } catch { }
        listener.Close();
    }
}
