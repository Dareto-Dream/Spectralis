using System.Text;
using System.Globalization;
using System.Text.Json;
using Markdig;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace Spectralis;

public sealed class EmbeddedContentControl : Control
{
    private const string AssetHostName = "spectralis-assets.local";
    private const string WorldHostName = "spectral-world.local";

    private WebView2? webView;
    private readonly string assetRootFolder;
    private bool initializationFailed;
    private float currentVideoSeconds;
    private long nextAudioFrameSyncTick;
    private bool audioFrameSyncPending;
    private bool hasWorldContent;
    private long nextWorldFrameSyncTick;
    private bool worldFrameSyncPending;
    private TaskCompletionSource? worldNavigationCompletion;

    public event EventHandler<CoreWebView2WebMessageReceivedEventArgs>? WorldMessageReceived;

    public EmbeddedContentControl()
    {
        assetRootFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Spectralis",
            "EmbeddedContentAssets");

        SetStyle(
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.UserPaint |
            ControlStyles.OptimizedDoubleBuffer,
            true);
        BackColor = Color.Black;
    }

    public bool IsReady => webView is not null && !initializationFailed;
    public bool HasContent { get; private set; }
    public bool CanSyncVideo { get; private set; }

    public async Task InitializeAsync()
    {
        if (webView is not null || initializationFailed)
        {
            return;
        }

        try
        {
            webView = new WebView2
            {
                AllowExternalDrop = false,
                Dock = DockStyle.Fill
            };
            Controls.Add(webView);

            var userDataFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Spectralis",
                "WebView2Cache");

            Directory.CreateDirectory(userDataFolder);

            var env = await CoreWebView2Environment.CreateAsync(null, userDataFolder);
            await webView.EnsureCoreWebView2Async(env);

            // Security settings
            webView.CoreWebView2.Settings.AreDevToolsEnabled = false;
            webView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
            webView.CoreWebView2.Settings.IsScriptEnabled = true;
            webView.CoreWebView2.Settings.IsWebMessageEnabled = true;
            webView.CoreWebView2.Settings.IsZoomControlEnabled = false;
            Directory.CreateDirectory(assetRootFolder);
            webView.CoreWebView2.SetVirtualHostNameToFolderMapping(
                AssetHostName,
                assetRootFolder,
                CoreWebView2HostResourceAccessKind.DenyCors);

            // Listen for navigation requests (block external URLs)
            webView.CoreWebView2.NavigationStarting += (s, e) =>
            {
                if (!IsAllowedNavigation(e.Uri))
                {
                    e.Cancel = true;
                }
            };

            webView.CoreWebView2.NewWindowRequested += (_, e) =>
            {
                e.Handled = true;
            };

            webView.CoreWebView2.NavigationCompleted += (_, _) =>
            {
                worldNavigationCompletion?.TrySetResult();
            };
        }
        catch
        {
            initializationFailed = true;
        }
    }

    public void LoadHtmlContent(EmbeddedHtmlContext context)
    {
        if (!IsReady || context is null)
        {
            return;
        }

        try
        {
            LeaveWorldMode();
            var html = Encoding.UTF8.GetString(context.HtmlBytes);
            var sanitized = SanitizeHtml(html);
            var withAssets = ResolveAssetReferences(sanitized, context.BinaryAssets, context.TextAssets);
            var withCsp = InjectContentSecurityPolicy(withAssets);

            webView!.CoreWebView2.NavigateToString(withCsp);
            HasContent = true;
            CanSyncVideo = false;
        }
        catch
        {
            HasContent = false;
            CanSyncVideo = false;
        }
    }

    public void LoadMarkdownContent(EmbeddedMarkdownContext context)
    {
        if (!IsReady || context is null)
        {
            return;
        }

        try
        {
            LeaveWorldMode();
            var markdown = Encoding.UTF8.GetString(context.MarkdownBytes);
            var html = Markdown.ToHtml(markdown);

            var template = new StringBuilder();
            template.AppendLine("<!DOCTYPE html>");
            template.AppendLine("<html>");
            template.AppendLine("<head>");
            template.AppendLine("  <meta charset=\"utf-8\">");
            template.AppendLine("  <meta name=\"viewport\" content=\"width=device-width, initial-scale=1.0\">");
            template.AppendLine("  <style>");
            template.AppendLine("    body {");
            template.AppendLine("      font-family: Georgia, serif;");
            template.AppendLine("      line-height: 1.6;");
            template.AppendLine("      padding: 2em;");
            template.AppendLine("      color: #e0e0e0;");
            template.AppendLine("      background-color: #1e1e1e;");
            template.AppendLine("      margin: 0;");
            template.AppendLine("    }");
            template.AppendLine("    h1, h2, h3, h4, h5, h6 {");
            template.AppendLine("      font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', sans-serif;");
            template.AppendLine("      color: #fff;");
            template.AppendLine("    }");
            template.AppendLine("    a { color: #4a9eff; }");
            template.AppendLine("    code { background-color: #2d2d2d; padding: 0.2em 0.4em; border-radius: 3px; }");
            template.AppendLine("    pre { background-color: #2d2d2d; padding: 1em; border-radius: 5px; overflow-x: auto; }");
            template.AppendLine("  </style>");

            if (!string.IsNullOrWhiteSpace(context.CssOverride))
            {
                template.AppendLine("  <style>");
                template.AppendLine(context.CssOverride);
                template.AppendLine("  </style>");
            }

            template.AppendLine("</head>");
            template.AppendLine("<body>");
            template.AppendLine(html);
            template.AppendLine("</body>");
            template.AppendLine("</html>");

            var finalHtml = InjectContentSecurityPolicy(template.ToString());
            webView!.CoreWebView2.NavigateToString(finalHtml);
            HasContent = true;
            CanSyncVideo = false;
        }
        catch
        {
            HasContent = false;
            CanSyncVideo = false;
        }
    }

    public void LoadVideoContent(EmbeddedVideoContext context)
    {
        if (!IsReady || context is null)
        {
            return;
        }

        try
        {
            LeaveWorldMode();
            var base64Video = Convert.ToBase64String(context.VideoBytes);
            var mimeType = GetMimeTypeForCodec(context.Codec);
            var dataUri = $"data:{mimeType};base64,{base64Video}";

            var width = context.Width ?? 1280;
            var height = context.Height ?? 720;
            var autoplay = context.Autoplay ? "autoplay" : "";
            var loop = context.Loop ? "loop" : "";

            var html = new StringBuilder();
            html.AppendLine("<!DOCTYPE html>");
            html.AppendLine("<html>");
            html.AppendLine("<head>");
            html.AppendLine("  <meta charset=\"utf-8\">");
            html.AppendLine("  <style>");
            html.AppendLine("    body { margin: 0; padding: 0; background-color: #000; display: flex; align-items: center; justify-content: center; min-height: 100vh; }");
            html.AppendLine("    video { max-width: 100%; max-height: 100vh; }");
            html.AppendLine("  </style>");
            html.AppendLine("</head>");
            html.AppendLine("<body>");
            html.AppendLine($"  <video width=\"{width}\" height=\"{height}\" controls {autoplay} {loop}>");
            html.AppendLine($"    <source src=\"{dataUri}\" type=\"{mimeType}\">");
            html.AppendLine("    Your browser does not support the video tag.");
            html.AppendLine("  </video>");
            html.AppendLine("  <script>");
            html.AppendLine("    window.currentVideoElement = document.querySelector('video');");
            html.AppendLine("    window.syncVideoPosition = function(seconds) {");
            html.AppendLine("      if (window.currentVideoElement) {");
            html.AppendLine("        window.currentVideoElement.currentTime = Math.max(0, Math.min(seconds, window.currentVideoElement.duration));");
            html.AppendLine("      }");
            html.AppendLine("    };");
            html.AppendLine("  </script>");
            html.AppendLine("</body>");
            html.AppendLine("</html>");

            var finalHtml = InjectContentSecurityPolicy(html.ToString());
            webView!.CoreWebView2.NavigateToString(finalHtml);
            HasContent = true;
            CanSyncVideo = true;
        }
        catch
        {
            HasContent = false;
            CanSyncVideo = false;
        }
    }

    public async Task SyncVideoPosition(float audioSeconds)
    {
        if (!IsReady || !HasContent || !CanSyncVideo)
        {
            return;
        }

        // Only update if position changed significantly (avoid too frequent updates)
        if (Math.Abs(audioSeconds - currentVideoSeconds) < 0.05f)
        {
            return;
        }

        currentVideoSeconds = audioSeconds;

        try
        {
            await webView!.CoreWebView2.ExecuteScriptAsync($"window.syncVideoPosition({audioSeconds});");
        }
        catch
        {
            // Ignore script execution errors
        }
    }

    public async Task SyncAudioFrame(VisualizerFrame frame, bool activePlayback, float position)
    {
        if (!IsReady || !HasContent || CanSyncVideo || audioFrameSyncPending)
        {
            return;
        }

        var now = Environment.TickCount64;
        if (now < nextAudioFrameSyncTick)
        {
            return;
        }

        nextAudioFrameSyncTick = now + 33;
        audioFrameSyncPending = true;

        try
        {
            var payload = new
            {
                levels = SampleSpectrum(frame.Spectrum, 32),
                peak = Math.Clamp(frame.PeakLevel, 0, 1.25f),
                rms = Math.Clamp(frame.RmsLevel, 0, 1.25f),
                active = activePlayback,
                time = position
            };
            var json = JsonSerializer.Serialize(payload);
            var script = string.Format(
                CultureInfo.InvariantCulture,
                """
                (() => {{
                  const frame = {0};
                  const bars = document.querySelectorAll('[data-audio-bars] span, .spectrum span');
                  for (let index = 0; index < bars.length; index += 1) {{
                    const value = Math.max(0, Math.min(1.25, frame.levels[index] || 0));
                    const idleFloor = frame.active ? 0.04 : 0.025;
                    const height = Math.round((idleFloor + value * 0.96) * 100);
                    bars[index].style.height = `${{Math.max(5, height)}}%`;
                    bars[index].style.opacity = String(frame.active ? Math.min(1, 0.38 + value * 0.72) : 0.26);
                    bars[index].style.transform = `scaleY(${{frame.active ? 0.86 + value * 0.28 : 0.55}})`;
                  }}

                  document.documentElement.style.setProperty('--audio-peak', String(Math.max(0, Math.min(1, frame.peak))));
                  document.documentElement.style.setProperty('--audio-rms', String(Math.max(0, Math.min(1, frame.rms))));
                  document.documentElement.style.setProperty('--audio-time', String(frame.time));
                  document.documentElement.classList.toggle('audio-active', Boolean(frame.active));
                  if (typeof window.onSpectralisFrame === 'function') window.onSpectralisFrame(frame);
                  if (typeof window.onAudioTime === 'function') window.onAudioTime(frame.time);
                }})();
                """,
                json);

            await webView!.CoreWebView2.ExecuteScriptAsync(script);
        }
        catch
        {
            // Ignore frame sync failures; embedded content should never interrupt playback.
        }
        finally
        {
            audioFrameSyncPending = false;
        }
    }

    public void LoadWorldContent(string worldFolder, string entryFile)
    {
        if (!IsReady)
            return;

        try
        {
            webView!.CoreWebView2.SetVirtualHostNameToFolderMapping(
                WorldHostName,
                worldFolder,
                CoreWebView2HostResourceAccessKind.DenyCors);

            webView.CoreWebView2.WebMessageReceived -= OnWorldWebMessageReceived;
            webView.CoreWebView2.WebMessageReceived += OnWorldWebMessageReceived;

            var normalizedEntry = entryFile.TrimStart('/').Replace('\\', '/');
            worldNavigationCompletion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            webView.CoreWebView2.Navigate($"https://{WorldHostName}/{normalizedEntry}");
            HasContent = true;
            CanSyncVideo = false;
            hasWorldContent = true;
        }
        catch
        {
            HasContent = false;
            hasWorldContent = false;
            worldNavigationCompletion = null;
        }
    }

    private void LeaveWorldMode()
    {
        if (!hasWorldContent || webView is null)
            return;

        try
        {
            webView.CoreWebView2.WebMessageReceived -= OnWorldWebMessageReceived;
        }
        catch { }

        hasWorldContent = false;
        nextWorldFrameSyncTick = 0;
        worldFrameSyncPending = false;
        worldNavigationCompletion = null;
    }

    public async Task ExecuteWorldScriptAsync(string script)
    {
        if (!IsReady || !hasWorldContent || string.IsNullOrWhiteSpace(script))
            return;

        try
        {
            await webView!.CoreWebView2.ExecuteScriptAsync(script);
        }
        catch { }
    }

    public async Task ClearWorldStorageAndReloadAsync()
    {
        if (!IsReady || !hasWorldContent || webView?.CoreWebView2 is null)
            return;

        await WaitForWorldNavigationAsync(TimeSpan.FromSeconds(2));

        try
        {
            worldNavigationCompletion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            await webView.CoreWebView2.ExecuteScriptAsync(
                "try { localStorage.clear(); sessionStorage.clear(); location.reload(); } catch (_) {}");
            await WaitForWorldNavigationAsync(TimeSpan.FromSeconds(2));
        }
        catch { }
    }

    public async Task SyncWorldFrame(VisualizerFrame frame, bool playing, float position, string frameScript)
    {
        if (!IsReady || !hasWorldContent || worldFrameSyncPending)
            return;

        var now = Environment.TickCount64;
        if (now < nextWorldFrameSyncTick)
            return;

        nextWorldFrameSyncTick = now + 33;
        worldFrameSyncPending = true;

        try
        {
            await webView!.CoreWebView2.ExecuteScriptAsync(frameScript);
        }
        catch { }
        finally
        {
            worldFrameSyncPending = false;
        }
    }

    private void OnWorldWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        WorldMessageReceived?.Invoke(this, e);
    }

    public void Clear()
    {
        if (!IsReady)
        {
            return;
        }

        try
        {
            if (hasWorldContent && webView is not null)
                webView.CoreWebView2.WebMessageReceived -= OnWorldWebMessageReceived;

            webView!.CoreWebView2.NavigateToString("<html><body></body></html>");
            HasContent = false;
            CanSyncVideo = false;
            hasWorldContent = false;
            worldNavigationCompletion = null;
            currentVideoSeconds = 0;
            nextAudioFrameSyncTick = 0;
            nextWorldFrameSyncTick = 0;
        }
        catch
        {
            // Ignore
        }
    }

    protected override void OnHandleDestroyed(EventArgs e)
    {
        webView?.Dispose();
        base.OnHandleDestroyed(e);
    }

    private async Task WaitForWorldNavigationAsync(TimeSpan timeout)
    {
        var navigationTask = worldNavigationCompletion?.Task;
        if (navigationTask is null || navigationTask.IsCompleted)
            return;

        try
        {
            await Task.WhenAny(navigationTask, Task.Delay(timeout));
        }
        catch { }
    }

    private static string SanitizeHtml(string html)
    {
        // Strip inline on* event handler attributes; inline <script> blocks are permitted
        // because the CSP (script-src 'unsafe-inline') and navigation blocking already sandbox
        // the content, and first-party visualizers need JS for interactive features.
        return System.Text.RegularExpressions.Regex.Replace(
            html,
            @"\s+on\w+\s*=\s*[""']?[^""']*[""']?",
            "",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    }

    private string ResolveAssetReferences(
        string html,
        IReadOnlyDictionary<string, byte[]> binaryAssets,
        IReadOnlyDictionary<string, string> textAssets)
    {
        if (binaryAssets.Count == 0 && textAssets.Count == 0)
        {
            return html;
        }

        var assetUris = PrepareAssetUris(binaryAssets);
        var resolvedBinaryAssets = System.Text.RegularExpressions.Regex.Replace(
            html,
            @"delta-(?:asset|bin):([A-Za-z0-9_.-]+)",
            match =>
            {
                var assetId = match.Groups[1].Value;
                if (assetUris.TryGetValue(assetId, out var assetUri))
                {
                    return assetUri;
                }

                if (!binaryAssets.TryGetValue(assetId, out var bytes))
                {
                    return match.Value;
                }

                var mimeType = GetMimeTypeForImage(bytes);
                return mimeType is null
                    ? match.Value
                    : $"data:{mimeType};base64,{Convert.ToBase64String(bytes)}";
            },
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        return System.Text.RegularExpressions.Regex.Replace(
            resolvedBinaryAssets,
            @"""?delta-data-json:([A-Za-z0-9_.-]+)""?",
            match =>
            {
                var assetId = match.Groups[1].Value;
                return textAssets.TryGetValue(assetId, out var text)
                    ? JsonSerializer.Serialize(text)
                    : "null";
            },
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    }

    private IReadOnlyDictionary<string, string> PrepareAssetUris(IReadOnlyDictionary<string, byte[]> binaryAssets)
    {
        var assetUris = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!IsReady)
        {
            return assetUris;
        }

        try
        {
            Directory.CreateDirectory(assetRootFolder);

            foreach (var (assetId, bytes) in binaryAssets)
            {
                if (string.IsNullOrWhiteSpace(assetId) || bytes.Length == 0)
                {
                    continue;
                }

                var extension = GetFileExtensionForAsset(bytes);
                if (extension is null)
                {
                    continue;
                }

                var fileName = $"{SanitizeAssetFileName(assetId)}-{ComputeAssetHash(bytes)}{extension}";
                var filePath = Path.Combine(assetRootFolder, fileName);
                if (!File.Exists(filePath))
                {
                    File.WriteAllBytes(filePath, bytes);
                }

                assetUris[assetId] = $"https://{AssetHostName}/{fileName}";
            }
        }
        catch
        {
            assetUris.Clear();
        }

        return assetUris;
    }

    private static string InjectContentSecurityPolicy(string html)
    {
        var csp = $"default-src 'none'; style-src 'unsafe-inline' 'self'; script-src 'unsafe-inline'; img-src data: blob: https://{AssetHostName}; font-src data:; media-src data: https://{AssetHostName}; video-src data: https://{AssetHostName}";
        var metaCsp = $"<meta http-equiv=\"Content-Security-Policy\" content=\"{csp}\">";

        if (html.Contains("</head>", StringComparison.OrdinalIgnoreCase))
        {
            return html.Replace("</head>", $"{metaCsp}\n</head>", StringComparison.OrdinalIgnoreCase);
        }

        if (html.Contains("<html>", StringComparison.OrdinalIgnoreCase))
        {
            return html.Replace("<html>", $"<html><head>{metaCsp}</head>", StringComparison.OrdinalIgnoreCase);
        }

        return $"<html><head>{metaCsp}</head><body>{html}</body></html>";
    }

    private static bool IsAllowedNavigation(string uri)
    {
        if (uri.StartsWith("about:blank", StringComparison.OrdinalIgnoreCase) ||
            uri.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (!Uri.TryCreate(uri, UriKind.Absolute, out var parsed))
            return false;

        if (!string.Equals(parsed.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            return false;

        return string.Equals(parsed.Host, AssetHostName, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(parsed.Host, WorldHostName, StringComparison.OrdinalIgnoreCase);
    }

    private static string GetMimeTypeForCodec(string codec)
    {
        return codec.ToLowerInvariant() switch
        {
            "h264" or "h.264" => "video/mp4",
            "h265" or "h.265" => "video/mp4",
            "vp9" => "video/webm",
            "av1" => "video/mp4",
            _ => "video/mp4"
        };
    }

    private static string? GetMimeTypeForImage(byte[] bytes)
    {
        if (bytes.Length >= 4 &&
            bytes[0] == 0xFF &&
            bytes[1] == 0xD8 &&
            bytes[2] == 0xFF)
        {
            return "image/jpeg";
        }

        if (bytes.Length >= 8 &&
            bytes[0] == 0x89 &&
            bytes[1] == 0x50 &&
            bytes[2] == 0x4E &&
            bytes[3] == 0x47)
        {
            return "image/png";
        }

        if (bytes.Length >= 12 &&
            bytes[0] == 0x52 &&
            bytes[1] == 0x49 &&
            bytes[2] == 0x46 &&
            bytes[3] == 0x46 &&
            bytes[8] == 0x57 &&
            bytes[9] == 0x45 &&
            bytes[10] == 0x42 &&
            bytes[11] == 0x50)
        {
            return "image/webp";
        }

        if (bytes.Length >= 6 &&
            bytes[0] == 0x47 &&
            bytes[1] == 0x49 &&
            bytes[2] == 0x46)
        {
            return "image/gif";
        }

        return null;
    }

    private static string? GetFileExtensionForAsset(byte[] bytes)
    {
        if (GetMimeTypeForImage(bytes) is { } imageMimeType)
        {
            return imageMimeType switch
            {
                "image/jpeg" => ".jpg",
                "image/png" => ".png",
                "image/webp" => ".webp",
                "image/gif" => ".gif",
                _ => null
            };
        }

        return GetMimeTypeForVideo(bytes) switch
        {
            "video/mp4" => ".mp4",
            "video/webm" => ".webm",
            "video/ogg" => ".ogv",
            _ => null
        };
    }

    private static string? GetMimeTypeForVideo(byte[] bytes)
    {
        if (bytes.Length >= 12 &&
            bytes[4] == 0x66 && bytes[5] == 0x74 && bytes[6] == 0x79 && bytes[7] == 0x70)
        {
            return "video/mp4";
        }

        if (bytes.Length >= 4 &&
            bytes[0] == 0x1A && bytes[1] == 0x45 && bytes[2] == 0xDF && bytes[3] == 0xA3)
        {
            return "video/webm";
        }

        if (bytes.Length >= 4 &&
            bytes[0] == 0x4F && bytes[1] == 0x67 && bytes[2] == 0x67 && bytes[3] == 0x53)
        {
            return "video/ogg";
        }

        return null;
    }

    private static string ComputeAssetHash(byte[] bytes) =>
        Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes))[..16].ToLowerInvariant();

    private static string SanitizeAssetFileName(string value)
    {
        var chars = value
            .Trim()
            .Select(static character =>
                char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or '.'
                    ? character
                    : '-')
            .ToArray();

        var sanitized = new string(chars).Trim('-', '.', '_');
        if (string.IsNullOrWhiteSpace(sanitized))
        {
            return "asset";
        }

        return sanitized.Length <= 80 ? sanitized : sanitized[..80];
    }

    private static float[] SampleSpectrum(float[] spectrum, int count)
    {
        var levels = new float[count];
        if (spectrum.Length == 0)
        {
            return levels;
        }

        for (var index = 0; index < levels.Length; index++)
        {
            var start = index * spectrum.Length / levels.Length;
            var end = Math.Max(start + 1, (index + 1) * spectrum.Length / levels.Length);
            var level = 0f;

            for (var sourceIndex = start; sourceIndex < end && sourceIndex < spectrum.Length; sourceIndex++)
            {
                level = Math.Max(level, Math.Clamp(spectrum[sourceIndex], 0, 1.25f));
            }

            levels[index] = level;
        }

        return levels;
    }
}
