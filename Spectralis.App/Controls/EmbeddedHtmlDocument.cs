using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Spectralis.Core.Embedded;
using Spectralis.Core.Integrations.Web;

namespace Spectralis.App.Controls;

/// <summary>Track metadata surfaced to an embedded HTML visualizer as <c>window.spectral.meta</c>.</summary>
public sealed record EmbeddedTrackMeta(
    string Title = "",
    string Artist = "",
    string Album = "",
    string AlbumArtist = "",
    string Genre = "",
    int Year = 0,
    int TrackNumber = 0,
    double DurationSeconds = 0,
    double? Bpm = null,
    string? MusicalKey = null,
    int SampleRate = 0,
    int Channels = 0,
    byte[]? ArtworkBytes = null,
    string? ArtworkMimeType = null)
{
    public static readonly EmbeddedTrackMeta Empty = new();
}

/// <summary>
/// Builds the HTML document served to an embedded visualizer / album-world surface:
/// asset resolution, inline-handler stripping, the performance prelude,
/// <c>window.spectral.meta</c>, the bridge bootstrap + frame bridge, and the CSP.
///
/// Shared by the live Now Playing surface (<see cref="Views.NowPlayingView"/>) and
/// the offscreen video-export WebView so both render capsules identically.
/// </summary>
public static class EmbeddedHtmlDocument
{
    /// <summary>Runs the full pipeline. <paramref name="onStage"/> receives (stageName, html) after each step.</summary>
    public static string Build(
        EmbeddedHtmlContext context,
        EmbeddedTrackMeta meta,
        bool isAlbumWorld = false,
        Action<string, string>? onStage = null,
        Action<string>? log = null)
    {
        var html = Encoding.UTF8.GetString(context.HtmlBytes);
        onStage?.Invoke("decoded", html);

        if (!isAlbumWorld)
        {
            html = StripInlineEventHandlers(html);
            onStage?.Invoke("stripped-inline-handlers", html);
        }

        html = ResolveAssetReferences(html, context.BinaryAssets, context.TextAssets, log);
        onStage?.Invoke("assets-resolved", html);

        html = InjectPerformancePrelude(html);
        onStage?.Invoke("performance-prelude", html);

        if (!isAlbumWorld)
        {
            html = InjectTrackMeta(html, meta);
            onStage?.Invoke("track-meta", html);
        }

        html = InjectBridgeBootstrap(html, isAlbumWorld);
        onStage?.Invoke("bridge-bootstrap", html);

        html = WebViewHostService.InjectContentSecurityPolicy(html, allowNetworkAccess: false);
        onStage?.Invoke("csp-final", html);

        return html;
    }

    public static string StripInlineEventHandlers(string html) =>
        Regex.Replace(
            html,
            "(?<script><script\\b[^>]*>[\\s\\S]*?</script>)|\\s+on\\w+\\s*=\\s*[\"']?[^\"']*[\"']?",
            match => match.Groups["script"].Success ? match.Value : string.Empty,
            RegexOptions.IgnoreCase);

    public static string ResolveAssetReferences(
        string html,
        IReadOnlyDictionary<string, byte[]> binaryAssets,
        IReadOnlyDictionary<string, string> textAssets,
        Action<string>? log = null)
    {
        if (binaryAssets.Count == 0 && textAssets.Count == 0)
        {
            log?.Invoke("[EMBEDDED] assets skipped no-assets");
            return html;
        }

        var binaryRefs = 0;
        var binaryResolved = 0;
        var binaryMissing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var withBinaryAssets = Regex.Replace(
            html,
            "delta-(?:asset|bin):([A-Za-z0-9_.-]+)",
            match =>
            {
                binaryRefs++;
                var assetId = match.Groups[1].Value;
                if (!binaryAssets.TryGetValue(assetId, out var bytes))
                {
                    binaryMissing.Add(assetId);
                    return match.Value;
                }

                binaryResolved++;
                return $"data:{GetMimeType(bytes, assetId)};base64,{Convert.ToBase64String(bytes)}";
            },
            RegexOptions.IgnoreCase);

        var textRefs = 0;
        var textResolved = 0;
        var textMissing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = Regex.Replace(
            withBinaryAssets,
            "\"?delta-data-json:([A-Za-z0-9_.-]+)\"?",
            match =>
            {
                textRefs++;
                var assetId = match.Groups[1].Value;
                if (textAssets.TryGetValue(assetId, out var text))
                {
                    textResolved++;
                    return JsonSerializer.Serialize(text);
                }

                textMissing.Add(assetId);
                return "null";
            },
            RegexOptions.IgnoreCase);

        log?.Invoke(
            $"[EMBEDDED] assets binaryRefs={binaryRefs} binaryResolved={binaryResolved} " +
            $"binaryMissing=[{string.Join(",", binaryMissing)}] textRefs={textRefs} textResolved={textResolved} " +
            $"textMissing=[{string.Join(",", textMissing)}]");
        return result;
    }

    public static string GetMimeType(byte[] bytes, string assetId)
    {
        if (bytes.Length >= 8 && bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47)
            return "image/png";

        if (bytes.Length >= 3 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF)
            return "image/jpeg";

        if (bytes.Length >= 6 && Encoding.ASCII.GetString(bytes, 0, 6) is "GIF87a" or "GIF89a")
            return "image/gif";

        if (bytes.Length >= 12 &&
            Encoding.ASCII.GetString(bytes, 0, 4) == "RIFF" &&
            Encoding.ASCII.GetString(bytes, 8, 4) == "WEBP")
            return "image/webp";

        return Path.GetExtension(assetId).ToLowerInvariant() switch
        {
            ".svg" => "image/svg+xml",
            ".woff" => "font/woff",
            ".woff2" => "font/woff2",
            ".mp4" => "video/mp4",
            ".webm" => "video/webm",
            ".json" => "application/json",
            ".css" => "text/css",
            ".js" => "text/javascript",
            _ => "application/octet-stream",
        };
    }

    public static string InjectPerformancePrelude(string html)
    {
        const string script =
            """
            <script>
            (() => {
              if (window.__spectralisPerformancePreludeInstalled) return;
              window.__spectralisPerformancePreludeInstalled = true;
              try {
                Object.defineProperty(window, "devicePixelRatio", {
                  get: function() { return 1; },
                  configurable: true
                });
              } catch {
              }
            })();
            </script>
            """;

        var headIndex = html.IndexOf("<head", StringComparison.OrdinalIgnoreCase);
        if (headIndex >= 0)
        {
            var headClose = html.IndexOf('>', headIndex);
            if (headClose >= 0)
                return html.Insert(headClose + 1, script);
        }

        return script + html;
    }

    public static string InjectTrackMeta(string html, EmbeddedTrackMeta meta)
    {
        string? artworkDataUrl = null;
        if (meta.ArtworkBytes is { Length: > 0 } art)
        {
            var mime = string.IsNullOrWhiteSpace(meta.ArtworkMimeType) ? "image/jpeg" : meta.ArtworkMimeType;
            artworkDataUrl = $"data:{mime};base64,{Convert.ToBase64String(art)}";
        }

        var metaJson = JsonSerializer.Serialize(new
        {
            title = meta.Title,
            artist = meta.Artist,
            album = meta.Album,
            albumArtist = meta.AlbumArtist,
            genre = meta.Genre,
            year = meta.Year,
            trackNumber = meta.TrackNumber,
            duration = meta.DurationSeconds,
            bpm = meta.Bpm,
            key = meta.MusicalKey,
            sampleRate = meta.SampleRate,
            channels = meta.Channels,
            artwork = artworkDataUrl,
        });

        var script = $"<script>window.spectral=window.spectral||{{}};window.spectral.meta={metaJson};</script>";

        var headIndex = html.IndexOf("<head", StringComparison.OrdinalIgnoreCase);
        if (headIndex >= 0)
        {
            var headClose = html.IndexOf('>', headIndex);
            if (headClose >= 0)
                return html.Insert(headClose + 1, script);
        }

        return script + html;
    }

    public static string InjectBridgeBootstrap(string html, bool isAlbumWorld = false)
    {
        var script = "<script>" + WebViewHostService.BuildBootstrapScript(isAlbumWorld) + BuildFrameBridgeScript() + "</script>";
        var bodyIndex = html.IndexOf("</body>", StringComparison.OrdinalIgnoreCase);
        if (bodyIndex >= 0)
            return html.Insert(bodyIndex, script);

        return html + script;
    }

    public static string BuildFrameBridgeScript() =>
        """
        (() => {
          if (window.__spectralisFrameBridgeInstalled) return;
          window.__spectralisFrameBridgeInstalled = true;

          // Pushed-frame slot: WebView2 path writes here; CefGlue path ignores it
          // because spectralisBridge.getFrameJson() always returns live data.
          let pushedFrame = null;
          window.__spectralisReceiveFrame = function(frame) {
            pushedFrame = frame;
            window.spectral._lastFrame = frame;
          };

          let bars = null;
          let nextBarsRefresh = 0;
          let interpBaseTime = 0;
          let interpBaseWall = 0;
          let interpActive = false;
          let lastAppliedTime = -1;

          function clamp(v, lo, hi) { return Math.max(lo, Math.min(hi, Number(v) || 0)); }

          function getBars() {
            const now = performance.now();
            if (!bars || now >= nextBarsRefresh) {
              bars = document.querySelectorAll('[data-audio-bars] span, .spectrum span');
              nextBarsRefresh = now + 1000;
            }
            return bars;
          }

          function applyFrame(frame, now) {
            const barNodes = getBars();
            const lvls = frame.levels || [];
            for (let i = 0; i < barNodes.length; i++) {
              const v = clamp(lvls[i], 0, 1.25);
              const floor = frame.active ? 0.04 : 0.025;
              barNodes[i].style.height = `${Math.max(5, Math.round((floor + v * 0.96) * 100))}%`;
              barNodes[i].style.opacity = String(frame.active ? Math.min(1, 0.38 + v * 0.72) : 0.26);
              barNodes[i].style.transform = `scaleY(${frame.active ? 0.86 + v * 0.28 : 0.55})`;
            }

            const t = Number(frame.time) || 0;
            const dur = window.spectral?.meta?.duration || 0;
            document.documentElement.style.setProperty('--audio-peak', String(clamp(frame.peak, 0, 1)));
            document.documentElement.style.setProperty('--audio-rms', String(clamp(frame.rms, 0, 1)));
            document.documentElement.style.setProperty('--audio-time', String(t));
            document.documentElement.style.setProperty('--spectral-progress', String(dur > 0 ? Math.min(1, t / dur) : 0));
            document.documentElement.classList.toggle('audio-active', Boolean(frame.active));

            interpBaseTime = t;
            interpBaseWall = now;
            interpActive = Boolean(frame.active);
            lastAppliedTime = t;

            if (typeof window.spectral?.onPlaybackFrame === 'function') window.spectral.onPlaybackFrame(frame);
            if (typeof window.onSpectralisFrame === 'function') window.onSpectralisFrame(frame);
            if (typeof window.onAudioTime === 'function') window.onAudioTime(frame.time);
          }

          // Apply spectral.meta once it's ready (injected at document-build time).
          function applyMetaOnce() {
            const meta = window.spectral?.meta;
            if (!meta) return;
            const dur = meta.duration || 0;
            if (dur > 0) {
              document.documentElement.style.setProperty('--spectral-duration', String(dur));
            }
          }
          if (document.readyState === 'loading') {
            document.addEventListener('DOMContentLoaded', applyMetaOnce, { once: true });
          } else {
            applyMetaOnce();
          }

          let rafFrames = 0;
          let rafWindowStart = performance.now();

          function pump(now) {
            rafFrames++;
            if (now - rafWindowStart >= 5000) {
              const fps = (rafFrames / (now - rafWindowStart) * 1000).toFixed(1);
              try {
                spectralisBridge.postMessage(JSON.stringify({
                  __rafStats: true, fps: parseFloat(fps), elapsed: Math.round(now - rafWindowStart)
                }));
              } catch {}
              rafFrames = 0;
              rafWindowStart = now;
            }

            // ── Frame acquisition (v5 pull-first model) ───────────────────
            // 1. Try spectralisBridge.getFrameJson() — live C# data on CefGlue,
            //    returns '' on WebView2 (which uses the push slot below).
            // 2. Fall back to pushedFrame written by window.__spectralisReceiveFrame.
            let frame = null;
            try {
              const raw = spectralisBridge.getFrameJson();
              if (raw) frame = JSON.parse(raw);
            } catch {}
            if (!frame) frame = pushedFrame;

            if (frame) {
              applyFrame(frame, now);
            }

            // Extrapolate --audio-time between frames for smooth CSS animations.
            if (interpActive && interpBaseWall > 0) {
              const extrapolated = interpBaseTime + (now - interpBaseWall) / 1000;
              document.documentElement.style.setProperty('--audio-time', String(extrapolated));
              const dur = window.spectral?.meta?.duration || 0;
              if (dur > 0) {
                document.documentElement.style.setProperty('--spectral-progress',
                  String(Math.min(1, extrapolated / dur)));
              }
            }

            requestAnimationFrame(pump);
          }

          requestAnimationFrame(pump);
        })();
        """;
}
