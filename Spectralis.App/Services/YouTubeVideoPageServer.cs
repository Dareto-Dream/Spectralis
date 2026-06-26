using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Spectralis.App.Services;

internal sealed class YouTubeVideoPageServer : IDisposable
{
    private readonly HttpListener _listener = new();
    private readonly CancellationTokenSource _cts = new();
    private readonly string _html;
    private bool _disposed;

    private YouTubeVideoPageServer(int port, string html)
    {
        _html = html;
        Url = $"http://127.0.0.1:{port}/youtube-video/";
        _listener.Prefixes.Add(Url);
    }

    public string Url { get; }

    public static YouTubeVideoPageServer Start(string videoId, double startSeconds, bool startPlaying)
    {
        var server = new YouTubeVideoPageServer(
            GetFreePort(),
            BuildYouTubeVideoPage(videoId, startSeconds, startPlaying));
        server._listener.Start();
        _ = server.AcceptLoopAsync(server._cts.Token);
        return server;
    }

    private async Task AcceptLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var context = await _listener.GetContextAsync().WaitAsync(cancellationToken);
                _ = Task.Run(() => ServeAsync(context), cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch
            {
            }
        }
    }

    private async Task ServeAsync(HttpListenerContext context)
    {
        try
        {
            if (!string.Equals(
                    context.Request.Url?.AbsolutePath,
                    "/youtube-video/",
                    StringComparison.OrdinalIgnoreCase))
            {
                Respond(context, 404, "text/plain; charset=utf-8", "Not found");
                return;
            }

            var bytes = Encoding.UTF8.GetBytes(_html);
            context.Response.ContentType = "text/html; charset=utf-8";
            context.Response.Headers["Cache-Control"] = "no-store";
            context.Response.ContentLength64 = bytes.Length;
            await context.Response.OutputStream.WriteAsync(bytes);
            context.Response.Close();
        }
        catch
        {
            try
            {
                context.Response.Close();
            }
            catch
            {
            }
        }
    }

    private static int GetFreePort()
    {
        using var socket = new TcpListener(IPAddress.Loopback, 0);
        socket.Start();
        return ((IPEndPoint)socket.LocalEndpoint).Port;
    }

    private static void Respond(HttpListenerContext context, int status, string contentType, string body)
    {
        var bytes = Encoding.UTF8.GetBytes(body);
        context.Response.StatusCode = status;
        context.Response.ContentType = contentType;
        context.Response.ContentLength64 = bytes.Length;
        try
        {
            context.Response.OutputStream.Write(bytes);
        }
        catch
        {
        }

        try
        {
            context.Response.Close();
        }
        catch
        {
        }
    }

    private static string BuildYouTubeVideoPage(string videoId, double startSeconds, bool startPlaying)
    {
        var start = Math.Max(0, (int)Math.Floor(startSeconds));
        var autoplay = startPlaying ? 1 : 0;
        var shouldPlay = startPlaying ? "true" : "false";
        var safeVideoId = JavaScriptString(videoId);
        return $$"""
            <!DOCTYPE html>
            <html>
            <head>
            <meta name="referrer" content="strict-origin-when-cross-origin">
            <style>
            * { margin: 0; padding: 0; box-sizing: border-box; }
            html, body { width: 100%; height: 100%; background: #000; overflow: hidden; }
            #player { width: 100%; height: 100%; }
            </style>
            </head>
            <body>
            <div id="player"></div>
            <script>
            var tag = document.createElement('script');
            tag.src = 'https://www.youtube.com/iframe_api';
            document.head.appendChild(tag);
            var player;
            function applyYouTubeReferrerPolicy() {
              var frame = document.querySelector('#player iframe');
              if (frame) frame.setAttribute('referrerpolicy', 'strict-origin-when-cross-origin');
            }
            new MutationObserver(applyYouTubeReferrerPolicy).observe(document.getElementById('player'), { childList: true, subtree: true });
            function onYouTubeIframeAPIReady() {
              player = new YT.Player('player', {
                videoId: '{{safeVideoId}}',
                playerVars: { autoplay: {{autoplay}}, mute: 1, start: {{start}}, controls: 1, modestbranding: 1, rel: 0, origin: window.location.origin },
                events: {
                  onReady: function(e) {
                    applyYouTubeReferrerPolicy();
                    e.target.mute();
                    e.target.setVolume(0);
                    if ({{shouldPlay}}) e.target.playVideo();
                    else e.target.pauseVideo();
                  }
                }
              });
            }
            window.ytpSync = function(seconds, shouldPlay) {
              if (!player || !player.getCurrentTime) return;
              player.mute && player.mute();
              player.setVolume && player.setVolume(0);
              var target = Math.max(0, Number(seconds) || 0);
              var current = player.getCurrentTime ? player.getCurrentTime() : 0;
              if (Math.abs(current - target) > 0.75 && player.seekTo) player.seekTo(target, true);
              if (shouldPlay && player.playVideo) player.playVideo();
              if (!shouldPlay && player.pauseVideo) player.pauseVideo();
            };
            window.ytpPause = function() { player && player.pauseVideo && player.pauseVideo(); };
            </script>
            </body>
            </html>
            """;
    }

    private static string JavaScriptString(string value) =>
        value
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("'", "\\'", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal)
            .Replace("\r", "", StringComparison.Ordinal)
            .Replace("\n", "", StringComparison.Ordinal);

    public static string FormatSeconds(double seconds) =>
        Math.Max(0, seconds).ToString("0.###", CultureInfo.InvariantCulture);

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _cts.Cancel();
        _cts.Dispose();
        try
        {
            if (_listener.IsListening)
            {
                _listener.Stop();
            }
        }
        catch
        {
        }

        _listener.Close();
    }
}
