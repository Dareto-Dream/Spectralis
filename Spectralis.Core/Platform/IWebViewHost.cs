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

    /// <summary>Raised for each postMessage payload from page script. The payload is
    /// untrusted; consumers must validate before acting.</summary>
    event EventHandler<string>? MessageReceived;

    /// <summary>Raised when the document finished loading (bootstrap injection point).</summary>
    event EventHandler? NavigationCompleted;

    /// <summary>Raised when the document fails to load. The embedded surface should fall back to the visualizer.</summary>
    event EventHandler? NavigationFailed;
}
