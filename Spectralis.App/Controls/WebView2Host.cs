#if WINDOWS
using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
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
        _core?.SetVirtualHostNameToFolderMapping(hostname, folderPath, kind);
    }

    public void Navigate(Uri url)
    {
        if (_core is not null)
            _core.Navigate(url.ToString());
        else
            _pendingUrl = url;
    }

    public void NavigateToString(string html)
    {
        if (_core is not null)
            _core.NavigateToString(html);
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

        _pendingScripts.Add(script);
        return Task.CompletedTask;
    }

    // IDisposable (NativeControlHost also handles native cleanup above) ---

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        CleanupCore();
    }

    // --- internals ---

    private async Task InitializeAsync()
    {
        try
        {
            var env = await GetOrCreateEnvironmentAsync(UserDataFolder, AdditionalBrowserArguments).ConfigureAwait(true);

            // Guard: DestroyNativeControlCore may have fired while we awaited the environment.
            if (_disposed || _hwnd == nint.Zero) return;

            AppLogPaths.AppendTimestamped(_log, $"[WV2] creating controller for HWND 0x{_hwnd:X}...");
            CoreWebView2Controller? controller = null;
            try
            {
                controller = await env.CreateCoreWebView2ControllerAsync(_hwnd).ConfigureAwait(true);
            }
            catch (ArgumentException) when (!_disposed && _hwnd != nint.Zero)
            {
                // The cached environment's browser process may have exited after all
                // controllers were closed (e.g. the host was briefly removed from the
                // visual tree). Evict the stale entry and retry once with a fresh env.
                AppLogPaths.AppendTimestamped(_log, "[WV2] controller create failed — evicting env cache and retrying...");
                env = await EvictAndRecreateEnvironmentAsync(UserDataFolder, AdditionalBrowserArguments).ConfigureAwait(true);
                if (_disposed || _hwnd == nint.Zero) return;
                controller = await env.CreateCoreWebView2ControllerAsync(_hwnd).ConfigureAwait(true);
            }
            _controller = controller;
            _core = _controller.CoreWebView2;

            // Stretch WebView2 to fill the HWND.
            SyncControllerBounds();

            // Expose spectralisBridge so the injected bridge script can postMessage back.
            // window.chrome.webview.postMessage is native in WebView2; we just alias it.
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

            _core.NavigationCompleted += (_, e) =>
            {
                if (e.IsSuccess)
                    NavigationCompleted?.Invoke(this, EventArgs.Empty);
                else
                    NavigationFailed?.Invoke(this, EventArgs.Empty);
            };

            // Register any virtual host mappings that arrived before init.
            foreach (var (host, (folder, kind)) in _virtualHosts)
                _core.SetVirtualHostNameToFolderMapping(host, folder, kind);

            // Drain pending script queue before navigation so they run on the right document.
            foreach (var script in _pendingScripts)
                _ = _core.ExecuteScriptAsync(script);
            _pendingScripts.Clear();

            // Navigate to queued content.
            if (_pendingHtml is not null)
            {
                _core.NavigateToString(_pendingHtml);
                _pendingHtml = null;
            }
            else if (_pendingUrl is not null)
            {
                _core.Navigate(_pendingUrl.ToString());
                _pendingUrl = null;
            }

            AppLogPaths.AppendTimestamped(_log, "[WV2] WebView2 initialized OK");
        }
        catch (Exception ex)
        {
            AppLogPaths.AppendTimestamped(_log, $"[WV2] init failed: {ex.GetType().Name}: {ex.Message}");
            Dispatcher.UIThread.Post(() => NavigationFailed?.Invoke(this, EventArgs.Empty));
        }
    }

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
