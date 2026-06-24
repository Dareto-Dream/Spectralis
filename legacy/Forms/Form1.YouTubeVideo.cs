using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace Spectralis;

public partial class Form1
{
    private bool youTubeVideoMode;
    private WebView2? youTubeVideoWebView;
    private YouTubeVideoPageServer? youTubeVideoPageServer;
    private System.Windows.Forms.Timer? youTubeVideoTimer;
    private bool youTubeVideoPollInProgress;
    private ModernButton? btnYouTubeVideo;

    private void InitializeYouTubeVideoUi()
    {
        btnYouTubeVideo = new ModernButton
        {
            IsGhost = true,
            Font = new System.Drawing.Font("Segoe UI", 9F),
            Margin = new System.Windows.Forms.Padding(20, 0, 0, 0),
            Size = new System.Drawing.Size(52, 28),
            AutoSize = false,
            Text = "Video",
            Visible = false
        };
        btnYouTubeVideo.Click += (_, _) => _ = ToggleYouTubeVideoModeAsync();
        visualizerNavPanel.Controls.Add(btnYouTubeVideo);
    }

    private Task ToggleYouTubeVideoModeAsync() =>
        youTubeVideoMode ? ExitYouTubeVideoModeAsync() : EnterYouTubeVideoModeAsync();

    private async Task EnterYouTubeVideoModeAsync()
    {
        if (!IsYouTubeActive || youTubeVideoMode) return;

        var videoId = youTubeCurrentTrack!.FilePath.StartsWith("youtube:", StringComparison.Ordinal)
            ? youTubeCurrentTrack.FilePath["youtube:".Length..]
            : null;
        if (videoId is null) return;

        var startSeconds = youTubePositionSeconds;
        var startPlaying = engine.IsPlaying;

        youTubeVideoMode = true;

        await EnsureYouTubeVideoWebViewAsync();

        if (youTubeVideoWebView is null)
        {
            youTubeVideoMode = false;
            engine.Play();
            return;
        }

        var videoPage = BuildYouTubeVideoPage(videoId, startSeconds, startPlaying);
        try
        {
            youTubeVideoPageServer?.Dispose();
            youTubeVideoPageServer = YouTubeVideoPageServer.Start(videoPage);
            youTubeVideoWebView.CoreWebView2.Navigate(youTubeVideoPageServer.Url);
        }
        catch
        {
            youTubeVideoWebView.NavigateToString(videoPage);
        }

        ApplyYouTubeVideoVisibility();

        youTubeVideoTimer = new System.Windows.Forms.Timer { Interval = 500 };
        youTubeVideoTimer.Tick += async (_, _) => await PollYouTubeVideoAsync();
        youTubeVideoTimer.Start();

        UpdateUiState();
    }

    private async Task ExitYouTubeVideoModeAsync()
    {
        if (!youTubeVideoMode) return;

        youTubeVideoTimer?.Stop();
        youTubeVideoTimer?.Dispose();
        youTubeVideoTimer = null;

        var audioPosition = engine.GetPosition();

        if (youTubeVideoWebView?.CoreWebView2 is { } wv)
        {
            try
            {
                await wv.ExecuteScriptAsync("window.ytpPause && window.ytpPause()");
            }
            catch { }

            youTubeVideoWebView.NavigateToString("<html><body style='background:#000'></body></html>");
        }
        youTubeVideoPageServer?.Dispose();
        youTubeVideoPageServer = null;

        youTubeVideoMode = false;
        ApplyYouTubeVideoVisibility();

        youTubePositionSeconds = audioPosition;
        youTubeIsPlaying = engine.IsPlaying;

        UpdateUiState();
    }

    private async Task EnsureYouTubeVideoWebViewAsync()
    {
        if (youTubeVideoWebView is not null) return;

        var webView = new WebView2 { Dock = DockStyle.Fill, Visible = false };
        contentLayout.Controls.Add(webView, 0, 0);

        try
        {
            var userDataFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Spectralis", "YouTubeVideoCache");
            Directory.CreateDirectory(userDataFolder);

            var options = new CoreWebView2EnvironmentOptions("--autoplay-policy=no-user-gesture-required");
            var env = await CoreWebView2Environment.CreateAsync(null, userDataFolder, options);
            await webView.EnsureCoreWebView2Async(env);

            webView.CoreWebView2.Settings.IsStatusBarEnabled = false;
            webView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
            webView.CoreWebView2.Settings.IsZoomControlEnabled = false;
        }
        catch
        {
            contentLayout.Controls.Remove(webView);
            webView.Dispose();
            return;
        }

        youTubeVideoWebView = webView;
    }

    private void ApplyYouTubeVideoVisibility()
    {
        if (youTubeVideoWebView is null) return;

        if (IsBrowserWorkspaceActive)
        {
            youTubeVideoWebView.Visible = false;
            visualizerControl.Visible = false;
            return;
        }

        youTubeVideoWebView.Visible = youTubeVideoMode;
        visualizerControl.Visible = !youTubeVideoMode;
        if (embeddedContentControl is not null)
            embeddedContentControl.Visible = false;

        if (youTubeVideoMode)
            youTubeVideoWebView.BringToFront();
        else
            visualizerControl.BringToFront();
    }

    private async Task PollYouTubeVideoAsync()
    {
        if (!youTubeVideoMode || youTubeVideoPollInProgress) return;
        if (youTubeVideoWebView?.CoreWebView2 is null) return;

        youTubeVideoPollInProgress = true;
        try
        {
            var json = await youTubeVideoWebView.CoreWebView2.ExecuteScriptAsync(
                "window.ytpGetState ? JSON.stringify(window.ytpGetState()) : 'null'");

            if (json is null or "null" or "\"null\"") return;

            // ExecuteScriptAsync returns a JSON-encoded string value, so unwrap quotes
            var raw = json.Length > 2 && json[0] == '"' ? json[1..^1].Replace("\\\"", "\"") : json;

            using var doc = JsonDocument.Parse(raw);
            var root = doc.RootElement;

            if (root.TryGetProperty("position", out var posEl) && posEl.TryGetSingle(out var pos))
            {
                var audioPosition = engine.GetPosition();
                var videoPlaying = root.TryGetProperty("paused", out var pausedEl) && !pausedEl.GetBoolean();
                var audioPlaying = engine.IsPlaying;
                if (Math.Abs(pos - audioPosition) > 0.75f || videoPlaying != audioPlaying)
                    await SyncYouTubeVideoFrameAsync(audioPosition, audioPlaying);
            }

            if (root.TryGetProperty("ended", out var endedEl) && endedEl.GetBoolean())
            {
                await SyncYouTubeVideoFrameAsync(engine.GetPosition(), engine.IsPlaying);
            }
        }
        catch { }
        finally
        {
            youTubeVideoPollInProgress = false;
        }
    }

    private async Task SyncYouTubeVideoFrameAsync(float positionSeconds, bool shouldPlay)
    {
        if (!youTubeVideoMode || youTubeVideoWebView?.CoreWebView2 is null)
            return;

        var position = Math.Max(0, positionSeconds).ToString(CultureInfo.InvariantCulture);
        var playing = shouldPlay ? "true" : "false";
        try
        {
            await youTubeVideoWebView.CoreWebView2.ExecuteScriptAsync(
                $"window.ytpSync && window.ytpSync({position}, {playing})");
        }
        catch { }
    }

    private static string BuildYouTubeVideoPage(string videoId, float startSeconds, bool startPlaying)
    {
        var start = (int)Math.Floor(startSeconds);
        var autoplay = startPlaying ? 1 : 0;
        var shouldPlay = startPlaying ? "true" : "false";
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
                videoId: '{{videoId}}',
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
            window.ytpGetPosition = function() { return player && player.getCurrentTime ? player.getCurrentTime() : 0; };
            window.ytpGetState = function() {
              if (!player || !player.getPlayerState) return null;
              var s = player.getPlayerState();
              return { position: player.getCurrentTime(), paused: s === 2 || s === 5, ended: s === 0 };
            };
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
            window.ytpPlay  = function() { player && player.playVideo  && player.playVideo();  };
            </script>
            </body>
            </html>
            """;
    }

    // Called synchronously from StopYouTubePlayback — no async position retrieval
    private void StopYouTubeVideoModeSync()
    {
        if (!youTubeVideoMode) return;

        youTubeVideoTimer?.Stop();
        youTubeVideoTimer?.Dispose();
        youTubeVideoTimer = null;
        youTubeVideoPollInProgress = false;
        youTubeVideoMode = false;

        try { _ = youTubeVideoWebView?.CoreWebView2?.ExecuteScriptAsync("window.ytpPause && window.ytpPause()"); }
        catch { }
        youTubeVideoWebView?.NavigateToString("<html><body style='background:#000'></body></html>");
        youTubeVideoPageServer?.Dispose();
        youTubeVideoPageServer = null;

        ApplyYouTubeVideoVisibility();
    }

    private void DisposeYouTubeVideo()
    {
        youTubeVideoTimer?.Stop();
        youTubeVideoTimer?.Dispose();
        youTubeVideoTimer = null;
        youTubeVideoMode = false;
        youTubeVideoPageServer?.Dispose();
        youTubeVideoPageServer = null;

        if (youTubeVideoWebView is not null)
        {
            contentLayout.Controls.Remove(youTubeVideoWebView);
            youTubeVideoWebView.Dispose();
            youTubeVideoWebView = null;
        }
    }

    private sealed class YouTubeVideoPageServer : IDisposable
    {
        private readonly HttpListener listener = new();
        private readonly CancellationTokenSource cts = new();
        private readonly string html;
        private bool disposed;

        private YouTubeVideoPageServer(int port, string html)
        {
            this.html = html;
            Url = $"http://127.0.0.1:{port}/youtube-video/";
            listener.Prefixes.Add(Url);
        }

        public string Url { get; }

        public static YouTubeVideoPageServer Start(string html)
        {
            var server = new YouTubeVideoPageServer(GetFreePort(), html);
            server.listener.Start();
            _ = server.AcceptLoopAsync(server.cts.Token);
            return server;
        }

        private async Task AcceptLoopAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    var ctx = await listener.GetContextAsync().WaitAsync(ct);
                    _ = Task.Run(() => ServeAsync(ctx), ct);
                }
                catch (OperationCanceledException) { break; }
                catch { }
            }
        }

        private async Task ServeAsync(HttpListenerContext ctx)
        {
            try
            {
                if (!string.Equals(ctx.Request.Url?.AbsolutePath, "/youtube-video/", StringComparison.OrdinalIgnoreCase))
                {
                    Respond(ctx, 404, "text/plain; charset=utf-8", "Not found");
                    return;
                }

                var bytes = Encoding.UTF8.GetBytes(html);
                ctx.Response.ContentType = "text/html; charset=utf-8";
                ctx.Response.Headers["Cache-Control"] = "no-store";
                ctx.Response.ContentLength64 = bytes.Length;
                await ctx.Response.OutputStream.WriteAsync(bytes);
                ctx.Response.Close();
            }
            catch
            {
                try { ctx.Response.Close(); } catch { }
            }
        }

        private static int GetFreePort()
        {
            using var socket = new TcpListener(IPAddress.Loopback, 0);
            socket.Start();
            return ((IPEndPoint)socket.LocalEndpoint).Port;
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
    }
}
