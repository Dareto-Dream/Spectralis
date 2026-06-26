namespace Spectralis;

internal sealed record InstalledVisualizerDefinition(
    string Id,
    string DisplayName,
    string? Version,
    EmbeddedVisualizerContext? Context,
    EmbeddedHtmlContext? HtmlContext)
{
    public bool IsHtml => HtmlContext is not null;
}

internal sealed record RedeemableVisualizerPackage(
    string Id,
    string DisplayName,
    string? Version,
    string ModuleJson,
    byte[] Binary,
    IReadOnlyDictionary<string, string> DataBlocks,
    IReadOnlyDictionary<string, byte[]> BinaryAssets,
    string Source);
