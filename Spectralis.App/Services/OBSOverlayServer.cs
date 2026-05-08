using System;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Spectralis.App.Services
{
    public class OBSOverlayServer : IDisposable
    {
        private readonly HttpListener _listener;
        private CancellationTokenSource? _cts;
        private OBSOverlayState _state = new();

        public const int DefaultPort = 5128;
        public bool IsRunning { get; private set; }

        public OBSOverlayServer(int port = DefaultPort)
        {
            _listener = new HttpListener();
            _listener.Prefixes.Add($"http://localhost:{port}/");
        }

        public void Start()
        {
            if (IsRunning) return;
            _cts = new CancellationTokenSource();
            _listener.Start();
            IsRunning = true;
            _ = ServeLoopAsync(_cts.Token);
        }

        public void Stop()
        {
            _cts?.Cancel();
            _listener.Stop();
            IsRunning = false;
        }

        public void UpdateState(OBSOverlayState state) => _state = state;

        private async Task ServeLoopAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    var ctx = await _listener.GetContextAsync();
                    _ = HandleRequestAsync(ctx);
                }
                catch (HttpListenerException) when (ct.IsCancellationRequested) { break; }
                catch { }
            }
        }

        private async Task HandleRequestAsync(HttpListenerContext ctx)
        {
            string path = ctx.Request.Url?.AbsolutePath ?? "/";
            ctx.Response.Headers.Add("Access-Control-Allow-Origin", "*");

            if (path == "/state")
            {
                string json = JsonSerializer.Serialize(_state);
                byte[] bytes = Encoding.UTF8.GetBytes(json);
                ctx.Response.ContentType = "application/json";
                ctx.Response.ContentLength64 = bytes.Length;
                await ctx.Response.OutputStream.WriteAsync(bytes);
            }
            else if (path == "/overlay")
            {
                string html = BuildOverlayHtml();
                byte[] bytes = Encoding.UTF8.GetBytes(html);
                ctx.Response.ContentType = "text/html";
                ctx.Response.ContentLength64 = bytes.Length;
                await ctx.Response.OutputStream.WriteAsync(bytes);
            }
            else
            {
                ctx.Response.StatusCode = 404;
            }
            ctx.Response.Close();
        }

        private string BuildOverlayHtml() => """
            <!DOCTYPE html>
            <html>
            <head><meta charset="utf-8"><style>
            body{margin:0;background:transparent;font-family:'Arial',sans-serif;color:white}
            #overlay{position:fixed;bottom:24px;left:24px;background:rgba(0,0,0,.6);
              padding:12px 20px;border-radius:8px;backdrop-filter:blur(8px)}
            #title{font-size:16px;font-weight:600}
            #artist{font-size:12px;color:#aaa;margin-top:2px}
            </style></head>
            <body>
            <div id="overlay"><div id="title">—</div><div id="artist">Spectralis</div></div>
            <script>
            async function poll(){
              const r=await fetch('/state');const s=await r.json();
              document.getElementById('title').textContent=s.trackTitle||'—';
              document.getElementById('artist').textContent=s.artist||'Spectralis';
            }
            poll();setInterval(poll,2000);
            </script>
            </body></html>
            """;

        public void Dispose()
        {
            Stop();
            _listener.Close();
            _cts?.Dispose();
        }
    }

    public class OBSOverlayState
    {
        public string TrackTitle { get; set; } = string.Empty;
        public string Artist { get; set; } = string.Empty;
        public string Album { get; set; } = string.Empty;
        public double PositionSeconds { get; set; }
        public double DurationSeconds { get; set; }
        public bool IsPlaying { get; set; }
        public string? CoverArtBase64 { get; set; }
    }
}
