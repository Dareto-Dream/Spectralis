using System.Reflection;
using Spectralis.App.Services;
using Spectralis.Core.Platform;
using WebViewControl;

namespace Spectralis.App.Controls;

/// <summary>
/// Production IWebViewHost over WebViewControl-Avalonia (CefGlue/Chromium) -
/// the uniform engine on all platforms per the BLOCKERS decision. A registered
/// bridge object receives postMessage payloads; a shim keeps capsule content
/// written against the legacy WebView2 `window.chrome.webview.postMessage`
/// working unchanged.
/// </summary>
public sealed class CefGlueWebViewHost : IWebViewHost
{
    private const string BridgeObjectName = "spectralisBridge";
    private static readonly string _perfLog = AppLogPaths.For("webview-perf.log");

    private readonly WebView _webView;
    private bool _disposed;

    public CefGlueWebViewHost(WebView webView)
    {
        _webView = webView;
        _webView.RegisterJavascriptObject(BridgeObjectName, new BridgeObject(this));
        _webView.Navigated += OnNavigated;
        _webView.LoadFailed += OnLoadFailed;
    }

    /// <summary>The wrapped Avalonia control, for placing in the visual tree.</summary>
    public WebView Control => _webView;

    public event EventHandler<string>? MessageReceived;
    public event EventHandler? NavigationCompleted;
    public event EventHandler? NavigationFailed;
    public Func<string>? FrameJsonProvider { get; set; }

    public void MapVirtualHost(string hostname, string folderPath)
    {
        // CefGlue serves local folders through its embedded resource scheme;
        // expose the mapping as https://{hostname}/ via a request interceptor.
        _webView.BeforeResourceLoad += resourceHandler =>
        {
            if (!Uri.TryCreate(resourceHandler.Url, UriKind.Absolute, out var uri) ||
                !string.Equals(uri.Host, hostname, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            var relative = uri.AbsolutePath.TrimStart('/');
            var fullPath = Path.GetFullPath(Path.Combine(folderPath, relative));

            // Containment: traversal outside the mapped folder is refused.
            if (!fullPath.StartsWith(Path.GetFullPath(folderPath), StringComparison.OrdinalIgnoreCase) ||
                !File.Exists(fullPath))
            {
                return;
            }

            resourceHandler.RespondWith(fullPath);
        };
    }

    public void Navigate(Uri url) => _webView.LoadUrl(url.ToString());

    public void NavigateToString(string html) => _webView.LoadHtml(html);

    public Task ExecuteScriptAsync(string script)
    {
        _webView.ExecuteScript(script);
        return Task.CompletedTask;
    }

    private void OnLoadFailed(string url, int errorCode, string frameName)
    {
        NavigationFailed?.Invoke(this, EventArgs.Empty);
    }

    private void OnNavigated(string url, string frameName)
    {
        // WebView2-compat shim: chrome.webview.postMessage routes to the bridge.
        _webView.ExecuteScript(
            $$"""
            window.chrome = window.chrome || {};
            window.chrome.webview = window.chrome.webview || {
                postMessage: function(msg) {
                    {{BridgeObjectName}}.postMessage(typeof msg === 'string' ? msg : JSON.stringify(msg));
                }
            };
            """);

        // Attempt to raise the OSR paint rate to 60fps so CSS animations and rAF
        // are not capped at CEF's 30fps default. Uses reflection because
        // WebViewControl.Avalonia has no public API for this.
        TrySetWindowlessFrameRate(60);

        NavigationCompleted?.Invoke(this, EventArgs.Empty);
    }

    private void TrySetWindowlessFrameRate(int fps)
    {
        try
        {
            const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Instance;
            // WebView.UnderlyingBrowser → ChromiumBrowser (internal)
            var chromiumBrowser = typeof(WebView)
                .GetProperty("UnderlyingBrowser", flags)
                ?.GetValue(_webView);
            if (chromiumBrowser is null)
            {
                AppLogPaths.AppendTimestamped(_perfLog, "[WFR] UnderlyingBrowser not found — skipping SetWindowlessFrameRate");
                return;
            }

            // ChromiumBrowser.GetBrowser() → CefBrowser (internal)
            var cefBrowser = chromiumBrowser.GetType()
                .GetMethod("GetBrowser", flags)
                ?.Invoke(chromiumBrowser, null) as Xilium.CefGlue.CefBrowser;
            if (cefBrowser is null)
            {
                AppLogPaths.AppendTimestamped(_perfLog, "[WFR] GetBrowser() returned null — browser not ready?");
                return;
            }

            cefBrowser.GetHost().SetWindowlessFrameRate(fps);
            AppLogPaths.AppendTimestamped(_perfLog, $"[WFR] SetWindowlessFrameRate({fps}) OK");
        }
        catch (Exception ex)
        {
            AppLogPaths.AppendTimestamped(_perfLog, $"[WFR] reflection failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    internal void RaiseMessage(string payload)
    {
        // Intercept rAF telemetry before routing to subscribers.
        if (payload.Contains("\"__rafStats\""))
        {
            AppLogPaths.AppendTimestamped(_perfLog, $"[RAF] {payload}");
            return;
        }

        MessageReceived?.Invoke(this, payload);
    }

    internal string GetFrameJson() => FrameJsonProvider?.Invoke() ?? string.Empty;

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _webView.Navigated -= OnNavigated;
        _webView.LoadFailed -= OnLoadFailed;
        _webView.Dispose();
    }

    /// <summary>Object bound into page script; CefGlue camel-cases member names.</summary>
    private sealed class BridgeObject
    {
        private readonly CefGlueWebViewHost _owner;

        public BridgeObject(CefGlueWebViewHost owner) => _owner = owner;

        // Page-callable: spectralisBridge.postMessage("{...}")
        public void PostMessage(string payload) => _owner.RaiseMessage(payload);

        // Page-callable: spectralisBridge.getFrameJson()
        public string GetFrameJson() => _owner.GetFrameJson();
    }
}
