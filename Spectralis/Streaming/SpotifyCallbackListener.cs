using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

namespace Spectralis.Streaming
{
    public class SpotifyCallbackListener : IDisposable
    {
        private readonly HttpListener _listener;
        private readonly string _redirectUri;
        private bool _disposed;

        public SpotifyCallbackListener(string redirectUri)
        {
            _redirectUri = redirectUri;
            _listener = new HttpListener();
            _listener.Prefixes.Add(redirectUri.TrimEnd('/') + "/");
        }

        public async Task<(string Code, string State)> WaitForCallbackAsync(CancellationToken ct = default)
        {
            _listener.Start();
            try
            {
                var getContext = _listener.GetContextAsync();
                var tcs = new TaskCompletionSource<bool>();
                using (ct.Register(() => tcs.TrySetCanceled()))
                {
                    var completed = await Task.WhenAny(getContext, tcs.Task);
                    if (completed == tcs.Task)
                        throw new OperationCanceledException(ct);
                }

                var context = await getContext;
                var query = context.Request.QueryString;
                string code = query["code"];
                string state = query["state"];

                var html = "<html><body><h2>Spectralis — Spotify connected!</h2><p>You can close this tab.</p></body></html>";
                var buf = System.Text.Encoding.UTF8.GetBytes(html);
                context.Response.ContentLength64 = buf.Length;
                await context.Response.OutputStream.WriteAsync(buf, 0, buf.Length, ct);
                context.Response.Close();

                return (code, state);
            }
            finally
            {
                _listener.Stop();
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            if (_listener.IsListening)
                _listener.Stop();
            _listener.Close();
        }
    }
}
