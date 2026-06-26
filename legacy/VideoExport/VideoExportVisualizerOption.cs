namespace Spectralis;

internal sealed record VideoExportVisualizerOption(
    string Key,
    string Label,
    VisualizerMode Mode,
    EmbeddedVisualizerContext? VisualizerContext = null,
    EmbeddedHtmlContext? HtmlContext = null,
    EmbeddedVideoContext? VideoContext = null)
{
    public bool UsesHtml => HtmlContext is not null;

    public bool UsesVideo => VideoContext is not null;

    public bool UsesWebView => UsesHtml || UsesVideo;

    public bool UsesEmbeddedRenderer => VisualizerContext is not null;

    public bool CanRenderInFrameExporter => !UsesWebView;

    public override string ToString() => Label;

    public static VideoExportVisualizerOption BuiltIn(VisualizerMode mode)
    {
        var definition = VisualizerCatalog.GetDefinition(mode);
        return new VideoExportVisualizerOption(
            VisualizerChoice.BuiltIn(definition.Mode).ToSettingsKey(),
            definition.Label,
            definition.Mode);
    }

    public static VideoExportVisualizerOption Embedded(EmbeddedVisualizerContext context) =>
        new(
            "track:embedded-visualizer",
            string.IsNullOrWhiteSpace(context.DisplayName)
                ? "Embedded Visualizer"
                : $"Embedded: {context.DisplayName}",
            VisualizerMode.MirrorSpectrum,
            VisualizerContext: context);

    public static VideoExportVisualizerOption EmbeddedHtml(EmbeddedHtmlContext context) =>
        new(
            "track:embedded-html",
            string.IsNullOrWhiteSpace(context.DisplayName)
                ? "Embedded HTML Visualizer"
                : $"Embedded HTML: {context.DisplayName}",
            VisualizerMode.MirrorSpectrum,
            HtmlContext: context);

    public static VideoExportVisualizerOption EmbeddedVideo(EmbeddedVideoContext context) =>
        new(
            "track:embedded-video",
            string.IsNullOrWhiteSpace(context.DisplayName)
                ? "Embedded Video"
                : $"Embedded Video: {context.DisplayName}",
            VisualizerMode.MirrorSpectrum,
            VideoContext: context);

    public static VideoExportVisualizerOption Installed(InstalledVisualizerDefinition definition)
    {
        var label = $"Special: {definition.DisplayName}";
        if (definition.Context is not null)
        {
            return new VideoExportVisualizerOption(
                VisualizerChoice.Installed(definition.Id).ToSettingsKey(),
                label,
                VisualizerMode.MirrorSpectrum,
                VisualizerContext: definition.Context);
        }

        return new VideoExportVisualizerOption(
            VisualizerChoice.Installed(definition.Id).ToSettingsKey(),
            label,
            VisualizerMode.MirrorSpectrum,
            HtmlContext: definition.HtmlContext);
    }
}
