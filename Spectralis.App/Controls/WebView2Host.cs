#if WINDOWS
using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;
using Avalonia.Threading;
using Microsoft.Web.WebView2.Core;
using Spectralis.App.Services;
using Spectralis.Core.Platform;

namespace Spectralis.App.Controls;

/// <summary>
/// IWebViewHost backed by Microsoft WebView2 (GPU-accelerated, DirectComposition) via
/// Avalonia NativeControlHost. Replaces the CefGlue OSR path for embedded HTML on
/// Windows so CSS animations and 3D transforms are GPU-composited instead of
/// software-rasterized.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WebView2Host : NativeControlHost, IWebViewHost
{
    private static readonly string _log = AppLogPaths.For("webview-perf.log");

    /// <summary>Optional persistent user data folder for the WebView2 environment.</summary>
    public string? UserDataFolder { get; init; }

    /// <summary>Optional additional browser arguments forwarded to the WebView2 environment.</summary>
    public string? AdditionalBrowserArguments { get; init; }

    // Multiple WebView2Host instances in the same process must share one CoreWebView2Environment
    // per (userDataFolder, browserArgs) pair. WebView2 does not allow two concurrent
    // CreateAsync calls against the same folder — only the first wins; others get E_INVALIDARG.
    // Lazy<Task<>> guarantees the factory runs exactly once even under concurrent access.
    private static readonly ConcurrentDictionary<string, Lazy<Task<CoreWebView2Environment>>> _envCache = new();

    private static Task<CoreWebView2Environment> GetOrCreateEnvironmentAsync(string? userDataFolder, string? additionalBrowserArguments)
    {
        var key = $"{userDataFolder ?? string.Empty}|{additionalBrowserArguments ?? string.Empty}";
        var lazy = _envCache.GetOrAdd(key, _ => new Lazy<Task<CoreWebView2Environment>>(() =>
        {
            CoreWebView2EnvironmentOptions? opts = string.IsNullOrEmpty(additionalBrowserArguments)
                ? null
                : new CoreWebView2EnvironmentOptions(additionalBrowserArguments);
            return CoreWebView2Environment.CreateAsync(null, userDataFolder, opts);
        }, isThreadSafe: true));
        return lazy.Value;
    }

    private static Task<CoreWebView2Environment> EvictAndRecreateEnvironmentAsync(string? userDataFolder, string? additionalBrowserArguments)
    {
        // Remove the potentially stale cached environment (browser process may have exited
        // after all controllers were closed) and force a fresh CreateAsync on next call.
        var key = $"{userDataFolder ?? string.Empty}|{additionalBrowserArguments ?? string.Empty}";
        _envCache.TryRemove(key, out _);
        return GetOrCreateEnvironmentAsync(userDataFolder, additionalBrowserArguments);
    }

    private nint _hwnd;
    private CoreWebView2Controller? _controller;
    private CoreWebView2? _core;
    private bool _disposed;
    private readonly string _largeHtmlHostName = $"spectralis-inline-{Guid.NewGuid():N}.local";
    private readonly string _largeHtmlFolder = Path.Combine(
        Path.GetTempPath(),
        "spectralis",
        "webview2-inline",
        Guid.NewGuid().ToString("N"));

    // Queued until WebView2 finishes async init
    private string? _pendingHtml;
    private Uri? _pendingUrl;
    private readonly List<string> _pendingScripts = [];
    private readonly Dictionary<string, (string folder, CoreWebView2HostResourceAccessKind kind)> _virtualHosts = new(StringComparer.OrdinalIgnoreCase);

    public event EventHandler<string>? MessageReceived;
    public event EventHandler? NavigationCompleted;
    public event EventHandler? NavigationFailed;

    // NativeControlHost interface ---

    protected override IPlatformHandle CreateNativeControlCore(IPlatformHandle parent)
    {
        // Create a blank child window that Avalonia will size/position for us.
        // WebView2 initializes async inside this HWND.
        const uint WS_CHILD = 0x40000000;
        const uint WS_VISIBLE = 0x10000000;
        const uint WS_CLIPCHILDREN = 0x02000000;

        _hwnd = NativeMethods.CreateWindowEx(
            0, "STATIC", null,
            WS_CHILD | WS_VISIBLE | WS_CLIPCHILDREN,
            0, 0, 1, 1,
            parent.Handle, nint.Zero, nint.Zero, nint.Zero);

        AppLogPaths.AppendTimestamped(_log, $"[WV2] NativeControlHost HWND created: 0x{_hwnd:X}");

        _ = InitializeAsync();

        return new PlatformHandle(_hwnd, "HWND");
    }

    protected override void DestroyNativeControlCore(IPlatformHandle control)
    {
        CleanupCore();
        if (_hwnd != nint.Zero)
        {
            NativeMethods.DestroyWindow(_hwnd);
            _hwnd = nint.Zero;
        }
    }

    // IWebViewHost ---

    public void MapVirtualHost(string hostname, string folderPath)
    {
        var kind = CoreWebView2HostResourceAccessKind.Allow;
        _virtualHosts[hostname] = (folderPath, kind);
        AppLogPaths.AppendTimestamped(_log,
            $"[WV2] map virtual host host={hostname} folder={folderPath} coreReady={_core is not null}");
        _core?.SetVirtualHostNameToFolderMapping(hostname, folderPath, kind);
    }

    public void Navigate(Uri url)
    {
        AppLogPaths.AppendTimestamped(_log,
            $"[WV2] navigate url={url} coreReady={_core is not null}");
        if (_core is not null)
            _core.Navigate(url.ToString());
        else
            _pendingUrl = url;
    }

    public void NavigateToString(string html)
    {
        LogHtmlNavigation("NavigateToString", html, _core is not null);
        if (_core is not null)
        {
            try
            {
                NavigateHtmlDocument(html, "NavigateToString");
            }
            catch (Exception ex)
            {
                LogNavigationException("NavigateToString", html, ex);
                throw;
            }
        }
        else
        {
            _pendingHtml = html;
            _pendingUrl = null;
        }
    }

    public Task ExecuteScriptAsync(string script)
    {
        if (_core is not null)
            return _core.ExecuteScriptAsync(script);

        AppLogPaths.AppendTimestamped(_log,
            $"[WV2] queue script chars={script.Length:n0} utf8={Encoding.UTF8.GetByteCount(script):n0}");
        _pendingScripts.Add(script);
        return Task.CompletedTask;
    }

    // IDisposable (NativeControlHost also handles native cleanup above) ---

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        CleanupCore();
        CleanupLargeHtmlFolder();
    }

    // --- internals ---

    private static void LogHtmlNavigation(string operation, string html, bool coreReady)
    {
        var utf8Bytes = Encoding.UTF8.GetByteCount(html);
        var sizeWarning = utf8Bytes >= 1_900_000 ? " near-or-over-WebView2-NavigateToString-limit" : string.Empty;
        AppLogPaths.AppendTimestamped(_log,
            $"[WV2] {operation} coreReady={coreReady} chars={html.Length:n0} utf8={utf8Bytes:n0}{sizeWarning}");
    }

    private static void LogNavigationException(string operation, string html, Exception ex)
    {
        var utf8Bytes = Encoding.UTF8.GetByteCount(html);
        AppLogPaths.AppendTimestamped(_log,
            $"[WV2] {operation} failed chars={html.Length:n0} utf8={utf8Bytes:n0} " +
            $"{ex.GetType().Name} 0x{ex.HResult:X8}: {ex.Message}");
    }

    private void NavigateHtmlDocument(string html, string operation)
    {
        var utf8Bytes = Encoding.UTF8.GetByteCount(html);
        if (utf8Bytes < 1_900_000)
        {
            _core!.NavigateToString(html);
            return;
        }

        Directory.CreateDirectory(_largeHtmlFolder);
        var indexPath = Path.Combine(_largeHtmlFolder, "index.html");
        File.WriteAllText(indexPath, html, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

        _core!.SetVirtualHostNameToFolderMapping(
            _largeHtmlHostName,
            _largeHtmlFolder,
            CoreWebView2HostResourceAccessKind.Allow);

        var url = $"https://{_largeHtmlHostName}/index.html?v={DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}";
        AppLogPaths.AppendTimestamped(_log,
            $"[WV2] {operation} using virtual-host file host={_largeHtmlHostName} " +
            $"path={indexPath} utf8={utf8Bytes:n0} url={url}");
        _core.Navigate(url);
    }

    private async Task InitializeAsync()
    {
        var stage = "environment";
        try
        {
            var env = await GetOrCreateEnvironmentAsync(UserDataFolder, AdditionalBrowserArguments).ConfigureAwait(true);

            // Guard: DestroyNativeControlCore may have fired while we awaited the environment.
            if (_disposed || _hwnd == nint.Zero) return;

            stage = "controller";
            AppLogPaths.AppendTimestamped(_log, $"[WV2] creating controller for HWND 0x{_hwnd:X}...");
            var controller = await CreateControllerWithRetryAsync(env).ConfigureAwait(true);
            _controller = controller;
            _core = _controller.CoreWebView2;

            // Stretch WebView2 to fill the HWND.
            stage = "bounds";
            SyncControllerBounds();

            // Expose spectralisBridge so the injected bridge script can postMessage back.
            // window.chrome.webview.postMessage is native in WebView2; we just alias it.
            stage = "bootstrap script";
            await _core.AddScriptToExecuteOnDocumentCreatedAsync("""
                (function() {
                  if (window.spectralisBridge) return;
                  window.spectralisBridge = {
                    postMessage: function(msg) {
                      try {
                        window.chrome.webview.postMessage(
                          typeof msg === 'string' ? msg : JSON.stringify(msg));
                      } catch {}
                    },
                    getFrameJson: function() { return ''; }
                  };
                })();
                """).ConfigureAwait(true);

            // Receive postMessage payloads from the page.
            // TryGetWebMessageAsString() returns the string when postMessage(string)
            // was used; falls back to the raw JSON representation otherwise.
            stage = "message handler";
            _core.WebMessageReceived += (_, e) =>
            {
                string msg;
                try { msg = e.TryGetWebMessageAsString(); }
                catch { msg = e.WebMessageAsJson; }

                // Intercept rAF telemetry before routing to subscribers — mirrors
                // CefGlueWebViewHost.RaiseMessage so [RAF] fps is visible regardless
                // of which host backs the embedded surface.
                // FIXME: __rafStats/__renderProf interception is debug instrumentation
                // from the embedded-visualizer lag investigation. Worth gating behind a
                // debug/verbose flag (or removing) once the time-smoothing fix has more
                // mileage across different hardware/refresh rates.
                if (msg.Contains("\"__rafStats\""))
                {
                    AppLogPaths.AppendTimestamped(_log, $"[RAF] {msg}");
                    return;
                }

                if (msg.Contains("\"__renderProf\""))
                {
                    AppLogPaths.AppendTimestamped(_log, $"[PROF] {msg}");
                    return;
                }

                MessageReceived?.Invoke(this, msg);
            };

            stage = "navigation handler";
            _core.NavigationCompleted += (_, e) =>
            {
                AppLogPaths.AppendTimestamped(_log,
                    $"[WV2] navigation completed success={e.IsSuccess} status={e.WebErrorStatus} source={_core.Source}");
                if (e.IsSuccess)
                    NavigationCompleted?.Invoke(this, EventArgs.Empty);
                else
                    NavigationFailed?.Invoke(this, EventArgs.Empty);
            };

            // Register any virtual host mappings that arrived before init.
            stage = "virtual host mappings";
            foreach (var (host, (folder, kind)) in _virtualHosts)
                _core.SetVirtualHostNameToFolderMapping(host, folder, kind);

            // Drain pending script queue before navigation so they run on the right document.
            stage = "pending scripts";
            foreach (var script in _pendingScripts)
                _ = _core.ExecuteScriptAsync(script);
            _pendingScripts.Clear();

            // Navigate to queued content.
            stage = "pending navigation";
            if (_pendingHtml is not null)
            {
                LogHtmlNavigation("pending NavigateToString", _pendingHtml, coreReady: true);
                try
                {
                    NavigateHtmlDocument(_pendingHtml, "pending NavigateToString");
                }
                catch (Exception ex)
                {
                    LogNavigationException("pending NavigateToString", _pendingHtml, ex);
                    throw;
                }

                _pendingHtml = null;
            }
            else if (_pendingUrl is not null)
            {
                AppLogPaths.AppendTimestamped(_log, $"[WV2] pending navigate url={_pendingUrl}");
                _core.Navigate(_pendingUrl.ToString());
                _pendingUrl = null;
            }

            AppLogPaths.AppendTimestamped(_log, "[WV2] WebView2 initialized OK");
        }
        catch (Exception ex)
        {
            AppLogPaths.AppendTimestamped(_log,
                $"[WV2] init failed at {stage}: {ex.GetType().Name} 0x{ex.HResult:X8}: {ex.Message}");
            AppLogPaths.AppendTimestamped(_log, ex.ToString());
            Dispatcher.UIThread.Post(() => NavigationFailed?.Invoke(this, EventArgs.Empty));
        }
    }

    private async Task<CoreWebView2Controller> CreateControllerWithRetryAsync(CoreWebView2Environment env)
    {
        const int maxAttempts = 3;

        for (var attempt = 1; attempt < maxAttempts; attempt++)
        {
            try
            {
                return await env.CreateCoreWebView2ControllerAsync(_hwnd).ConfigureAwait(true);
            }
            catch (Exception ex) when (CanRetryControllerCreate(ex) &&
                                       !_disposed &&
                                       _hwnd != nint.Zero)
            {
                AppLogPaths.AppendTimestamped(_log,
                    $"[WV2] controller create failed attempt {attempt}/{maxAttempts}: " +
                    $"{ex.GetType().Name} 0x{ex.HResult:X8}: {ex.Message}");

                CleanupCore();
                env = await EvictAndRecreateEnvironmentAsync(UserDataFolder, AdditionalBrowserArguments)
                    .ConfigureAwait(true);

                // Give Avalonia/Win32 one tick to finish parenting and sizing the child HWND.
                await Task.Delay(75).ConfigureAwait(true);
            }
        }

        return await env.CreateCoreWebView2ControllerAsync(_hwnd).ConfigureAwait(true);
    }

    private static bool CanRetryControllerCreate(Exception ex) =>
        ex is ArgumentException ||
        ex is COMException { HResult: unchecked((int)0x80070057) };

    private void SyncControllerBounds()
    {
        if (_controller is null || _hwnd == nint.Zero) return;

        // NativeControlHost sizes our HWND in device pixels via Win32.
        // Query the current client rect to get the exact pixel size.
        if (NativeMethods.GetClientRect(_hwnd, out var rect))
        {
            var w = rect.Right - rect.Left;
            var h = rect.Bottom - rect.Top;
            if (w > 0 && h > 0)
                _controller.Bounds = new System.Drawing.Rectangle(0, 0, w, h);
        }
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var result = base.ArrangeOverride(finalSize);
        // Avalonia has updated our HWND size by this point; tell WebView2 to fill it.
        SyncControllerBounds();
        return result;
    }

    private void CleanupCore()
    {
        _controller?.Close();
        _controller = null;
        _core = null;
    }

    private void CleanupLargeHtmlFolder()
    {
        try
        {
            if (Directory.Exists(_largeHtmlFolder))
                Directory.Delete(_largeHtmlFolder, recursive: true);
        }
        catch
        {
            // Temp debug/navigation files are best-effort cleanup.
        }
    }

    private static class NativeMethods
    {
        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        internal static extern nint CreateWindowEx(
            uint dwExStyle,
            string? lpClassName,
            string? lpWindowName,
            uint dwStyle,
            int x, int y, int nWidth, int nHeight,
            nint hWndParent, nint hMenu, nint hInstance, nint lpParam);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool DestroyWindow(nint hwnd);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool GetClientRect(nint hWnd, out RECT lpRect);

        [StructLayout(LayoutKind.Sequential)]
        internal struct RECT
        {
            public int Left, Top, Right, Bottom;
        }
    }
}
#endif
