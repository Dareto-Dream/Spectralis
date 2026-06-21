namespace Spectralis.Core.Visualizers.Installed;

public sealed record InstalledVisualizerDefinition(
    string Id,
    string DisplayName,
    string? Version,
    DateTimeOffset InstalledAtUtc);

/// <summary>Full loaded content of an installed visualizer, decoded and ready to use.</summary>
public sealed record InstalledVisualizerContent(
    string Id,
    string DisplayName,
    string? Version,
    byte[] HtmlBytes,
    IReadOnlyDictionary<string, string> TextAssets,
    IReadOnlyDictionary<string, byte[]> BinaryAssets);

public sealed record RedeemableVisualizerPackage(
    string Id,
    string DisplayName,
    string? Version,
    string ModuleJson,
    byte[] Binary,
    IReadOnlyDictionary<string, string> DataBlocks,
    IReadOnlyDictionary<string, byte[]> BinaryAssets,
    string Source);
