namespace Spectralis.App.Services;

/// <summary>
/// Dedicated debug log for the Wasm/wgpu album world pipeline (native renderer creation,
/// world attach/detach, and the input → render loop) — same idea as <c>spotify.log</c>/
/// <c>youtube.log</c>/<c>bandlab.log</c>, just for this runtime. Exists because the
/// generic <c>webview-perf.log</c> the wgpu surface used to borrow a line from is shared with
/// every WebView-hosted surface and gets noisy fast; this one is just the wgpu world's story,
/// from "did the native renderer even get a GPU adapter" down to "is a key event actually
/// reaching the control." Check it first when movement/look isn't responding.
/// </summary>
public static class WasmWorldLog
{
    public static string LogPath => AppLogPaths.For("wasm-world.log");

    public static void Log(string message) => AppLogPaths.AppendTimestamped(LogPath, message);
}
