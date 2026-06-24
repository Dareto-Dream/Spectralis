namespace Spectralis;

internal static class CapsuleCapability
{
    public const string AppThemeDeepControl    = "app.theme.deepControl";
    public const string AppLayoutDeepControl   = "app.layout.deepControl";
    public const string AppChromeEffects       = "app.chrome.effects";
    public const string VisualizerMultiLayer   = "visualizer.multiLayer";
    public const string VisualizerWasm         = "visualizer.wasm";
    public const string VisualizerShaderPack   = "visualizer.shaderPack";
    public const string WebViewLocalContent    = "webview.localContent";
    public const string WebViewNetworkAccess   = "webview.networkAccess";
    public const string SharedPlayHostCapsule  = "sharedPlay.hostCapsule";
    public const string SharedPlayPackageUpload = "sharedPlay.packageUpload";
    public const string TimelineAppControl     = "timeline.appControl";
    public const string AlbumWorld             = "album.world";

    public static readonly IReadOnlySet<string> All = new HashSet<string>
    {
        AppThemeDeepControl, AppLayoutDeepControl, AppChromeEffects,
        VisualizerMultiLayer, VisualizerWasm, VisualizerShaderPack,
        WebViewLocalContent, WebViewNetworkAccess,
        SharedPlayHostCapsule, SharedPlayPackageUpload, TimelineAppControl,
        AlbumWorld
    };
}
