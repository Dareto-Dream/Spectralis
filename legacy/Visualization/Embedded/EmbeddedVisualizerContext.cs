using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.Json.Nodes;

namespace Spectralis;

internal sealed record EmbeddedVisualizerModule(
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

internal sealed class EmbeddedDataBlock
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

        if (JsonValue is JsonObject jsonObject &&
            jsonObject[propertyName] is JsonValue jsonValue &&
            jsonValue.TryGetValue<double>(out var numericValue))
        {
            value = (float)numericValue;
            return true;
        }

        return float.TryParse(RawText, CultureInfo.InvariantCulture, out value);
    }

    public string? TryGetString(params string[] propertyNames)
    {
        if (JsonValue is JsonObject jsonObject)
        {
            foreach (var propertyName in propertyNames)
            {
                if (jsonObject[propertyName] is JsonValue jsonValue &&
                    jsonValue.TryGetValue<string>(out var stringValue) &&
                    !string.IsNullOrWhiteSpace(stringValue))
                {
                    return stringValue.Trim();
                }
            }
        }

        return string.IsNullOrWhiteSpace(RawText) ? null : RawText.Trim();
    }
}

public sealed class EmbeddedVisualizerContext
{
    private readonly IReadOnlyDictionary<string, EmbeddedDataBlock> dataBlocks;
    private readonly IReadOnlyDictionary<string, string> dataRefs;
    private readonly string? explicitDisplayName;
    private string? displayName;

    internal EmbeddedVisualizerContext(
        EmbeddedVisualizerModule module,
        byte[] binary,
        IDictionary<string, EmbeddedDataBlock> dataBlocks,
        string? displayName = null)
    {
        Module = module;
        Binary = binary.ToArray();
        explicitDisplayName = string.IsNullOrWhiteSpace(displayName) ? null : displayName.Trim();
        dataRefs = new ReadOnlyDictionary<string, string>(
            new Dictionary<string, string>(module.DataRefs, StringComparer.OrdinalIgnoreCase));
        this.dataBlocks = new ReadOnlyDictionary<string, EmbeddedDataBlock>(
            new Dictionary<string, EmbeddedDataBlock>(dataBlocks, StringComparer.OrdinalIgnoreCase));
    }

    internal EmbeddedVisualizerModule Module { get; }

    internal byte[] Binary { get; }

    internal IReadOnlyDictionary<string, EmbeddedDataBlock> DataBlocks => dataBlocks;

    internal string DisplayLabel => DisplayName;

    public string DisplayName => displayName ??= explicitDisplayName ?? CreateDisplayLabel(Module.Id);

    internal EmbeddedDataBlock? GetDataByReference(string referenceId) =>
        dataBlocks.TryGetValue(referenceId, out var dataBlock) ? dataBlock : null;

    internal EmbeddedDataBlock? GetDataByBinding(string bindingName) =>
        dataRefs.TryGetValue(bindingName, out var referenceId)
            ? GetDataByReference(referenceId)
            : null;

    private static string CreateDisplayLabel(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "Embedded Visualizer";
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
