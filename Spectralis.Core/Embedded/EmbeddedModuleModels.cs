using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.Json.Nodes;

namespace Spectralis.Core.Embedded;

public sealed record EmbeddedModuleInfo(
    string Id,
    string Type,
    string Runtime,
    string Entry,
    IReadOnlyDictionary<string, string> DataRefs,
    string BinaryRef,
    string? Version,
    int? Width = null,
    int? Height = null,
    bool Autoplay = false,
    bool Loop = true);

public sealed class EmbeddedDataBlock
{
    public EmbeddedDataBlock(string id, string rawText, JsonNode? jsonValue)
    {
        Id = id;
        RawText = rawText;
        JsonValue = jsonValue;
    }

    public string Id { get; }
    public string RawText { get; }
    public JsonNode? JsonValue { get; }

    public bool TryGetNumber(string propertyName, out float value)
    {
        value = 0;
        if (JsonValue is JsonObject obj &&
            obj[propertyName] is JsonValue jsonValue &&
            jsonValue.TryGetValue<double>(out var numericValue))
        {
            value = (float)numericValue;
            return true;
        }

        return float.TryParse(RawText, CultureInfo.InvariantCulture, out value);
    }

    public string? TryGetString(params string[] propertyNames)
    {
        if (JsonValue is JsonObject obj)
        {
            foreach (var name in propertyNames)
            {
                if (obj[name] is JsonValue value &&
                    value.TryGetValue<string>(out var text) &&
                    !string.IsNullOrWhiteSpace(text))
                {
                    return text.Trim();
                }
            }
        }

        return string.IsNullOrWhiteSpace(RawText) ? null : RawText.Trim();
    }
}

public sealed class EmbeddedVisualizerContext
{
    private readonly IReadOnlyDictionary<string, EmbeddedDataBlock> _dataBlocks;
    private readonly IReadOnlyDictionary<string, string> _dataRefs;
    private readonly string? _explicitDisplayName;
    private string? _displayName;

    public EmbeddedVisualizerContext(
        EmbeddedModuleInfo module,
        byte[] binary,
        IDictionary<string, EmbeddedDataBlock> dataBlocks,
        string? displayName = null)
    {
        Module = module;
        Binary = binary.ToArray();
        _explicitDisplayName = string.IsNullOrWhiteSpace(displayName) ? null : displayName.Trim();
        _dataRefs = new ReadOnlyDictionary<string, string>(
            new Dictionary<string, string>(module.DataRefs, StringComparer.OrdinalIgnoreCase));
        _dataBlocks = new ReadOnlyDictionary<string, EmbeddedDataBlock>(
            new Dictionary<string, EmbeddedDataBlock>(dataBlocks, StringComparer.OrdinalIgnoreCase));
    }

    public EmbeddedModuleInfo Module { get; }
    public byte[] Binary { get; }
    public IReadOnlyDictionary<string, EmbeddedDataBlock> DataBlocks => _dataBlocks;
    public string DisplayName => _displayName ??= _explicitDisplayName ?? CreateDisplayLabel(Module.Id, "Embedded Visualizer");

    public EmbeddedDataBlock? GetDataByReference(string referenceId) =>
        _dataBlocks.TryGetValue(referenceId, out var dataBlock) ? dataBlock : null;

    public EmbeddedDataBlock? GetDataByBinding(string bindingName) =>
        _dataRefs.TryGetValue(bindingName, out var referenceId) ? GetDataByReference(referenceId) : null;

    internal static string CreateDisplayLabel(string value, string fallback)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return fallback;
        }

        return string.Join(
            ' ',
            value
                .Replace('_', ' ')
                .Replace('-', ' ')
                .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(static part => char.ToUpperInvariant(part[0]) + part[1..]));
    }
}

public sealed class EmbeddedHtmlContext
{
    public EmbeddedHtmlContext(
        string id,
        byte[] htmlBytes,
        IReadOnlyDictionary<string, byte[]> binaryAssets,
        IReadOnlyDictionary<string, string>? textAssets,
        string? version,
        string? sourceDirectory = null)
    {
        Id = id;
        HtmlBytes = htmlBytes.ToArray();
        BinaryAssets = binaryAssets.ToDictionary(
            static item => item.Key,
            static item => item.Value.ToArray(),
            StringComparer.OrdinalIgnoreCase);
        TextAssets = (textAssets ?? new Dictionary<string, string>())
            .ToDictionary(static item => item.Key, static item => item.Value, StringComparer.OrdinalIgnoreCase);
        Version = version;
        SourceDirectory = sourceDirectory;
    }

    public string Id { get; }
    public byte[] HtmlBytes { get; }
    public IReadOnlyDictionary<string, byte[]> BinaryAssets { get; }
    public IReadOnlyDictionary<string, string> TextAssets { get; }
    public string? Version { get; }
    public string? SourceDirectory { get; }
    public string DisplayName => EmbeddedVisualizerContext.CreateDisplayLabel(Id, "HTML Content");
}

public sealed class EmbeddedMarkdownContext
{
    public EmbeddedMarkdownContext(string id, byte[] markdownBytes, string? cssOverride, string? version)
    {
        Id = id;
        MarkdownBytes = markdownBytes.ToArray();
        CssOverride = cssOverride;
        Version = version;
    }

    public string Id { get; }
    public byte[] MarkdownBytes { get; }
    public string? CssOverride { get; }
    public string? Version { get; }
    public string DisplayName => EmbeddedVisualizerContext.CreateDisplayLabel(Id, "Markdown Content");
}

public sealed class EmbeddedVideoContext
{
    public EmbeddedVideoContext(
        string id,
        string codec,
        byte[] videoBytes,
        int? width,
        int? height,
        bool autoplay,
        bool loop,
        string? version)
    {
        Id = id;
        Codec = codec;
        VideoBytes = videoBytes.ToArray();
        Width = width;
        Height = height;
        Autoplay = autoplay;
        Loop = loop;
        Version = version;
    }

    public string Id { get; }
    public string Codec { get; }
    public byte[] VideoBytes { get; }
    public int? Width { get; }
    public int? Height { get; }
    public bool Autoplay { get; }
    public bool Loop { get; }
    public string? Version { get; }
    public string DisplayName => EmbeddedVisualizerContext.CreateDisplayLabel(Id, "Video Content");
}

/// <summary>Embedded per-track theme override (mode + accent strings from DELTA_THEME tags).</summary>
public sealed record EmbeddedThemeInfo(string Mode, string Accent);

public sealed record EmbeddedModuleSet(
    EmbeddedVisualizerContext? Visualizer,
    EmbeddedHtmlContext? Html,
    EmbeddedMarkdownContext? Markdown,
    EmbeddedVideoContext? Video,
    EmbeddedThemeInfo? Theme = null)
{
    public static EmbeddedModuleSet Empty { get; } = new(null, null, null, null);
    public bool HasAny => Visualizer is not null || Html is not null || Markdown is not null || Video is not null;
    public string Summary =>
        string.Join(
            ", ",
            new[]
            {
                Visualizer?.DisplayName,
                Html?.DisplayName,
                Markdown?.DisplayName,
                Video?.DisplayName,
            }.Where(static value => !string.IsNullOrWhiteSpace(value)));
}
