namespace Spectralis.Core.Platform;

/// <summary>
/// Abstraction over the embedded browser (CefGlue/Chromium in production per
/// the BLOCKERS decision — uniform engine on all platforms). Capsule HTML,
/// .spectral album worlds, and embedded track HTML render through this.
/// </summary>
public interface IWebViewHost : IDisposable
{
    /// <summary>Maps https://{hostname}/ to a local folder. Capsule content is served
    /// through this mapping only — never via file:// — so the page has no
    /// filesystem identity.</summary>
    void MapVirtualHost(string hostname, string folderPath);

    void Navigate(Uri url);

    void NavigateToString(string html);

    Task ExecuteScriptAsync(string script);

    /// <summary>
    /// OS process id of the embedded browser (for per-process audio loopback capture),
    /// or null when the backend can't provide one / isn't initialized yet.
    /// </summary>
    int? BrowserProcessId => null;

    /// <summary>
    /// Mutes/unmutes the embedded browser's audio output. Used by the experimental
    /// Spotify-EQ path, which captures the browser audio, processes it, and re-plays it.
    /// No-op on backends that don't support it.
    /// </summary>
    bool AudioMuted
    {
        get => false;
        set { }
    }

    /// <summary>Forces the browser to re-sync its paint size to the host control's current
    /// bounds. Needed after an instant, single-jump resize (window maximize/restore) — both
    /// CEF's OSR WasResized()/GetViewRect() round-trip and WebView2's NativeControlHost bounds
    /// sync can race and settle on a stale size when there's no follow-up resize event to
    /// nudge them, unlike a manual drag-resize which sends many.</summary>
    void NudgeResize();

    /// <summary>Raised for each postMessage payload from page script. The payload is
    /// untrusted; consumers must validate before acting.</summary>
    event EventHandler<string>? MessageReceived;

    /// <summary>Raised when the document finished loading (bootstrap injection point).</summary>
    event EventHandler? NavigationCompleted;

    /// <summary>Raised when the document fails to load. The embedded surface should fall back to the visualizer.</summary>
    event EventHandler? NavigationFailed;
}
