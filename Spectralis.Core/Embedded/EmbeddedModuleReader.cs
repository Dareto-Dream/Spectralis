using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using TagLib;
using TagLib.Id3v2;

namespace Spectralis.Core.Embedded;

public static class EmbeddedModuleReader
{
    private const string ModulePrefix = "DELTA_MODULE_";
    private const string BinaryPrefix = "DELTA_BIN_";
    private const string DataPrefix = "DELTA_DATA_";

    public static EmbeddedModuleSet ReadFromAudioTags(string audioPath)
    {
        try
        {
            using var file = TagLib.File.Create(audioPath);
            return TryReadAll(file.GetTag(TagTypes.Id3v2, false) as TagLib.Id3v2.Tag);
        }
        catch
        {
            return EmbeddedModuleSet.Empty;
        }
    }

    public static EmbeddedModuleSet TryReadAll(TagLib.Id3v2.Tag? id3Tag)
    {
        if (id3Tag is null)
        {
            return EmbeddedModuleSet.Empty;
        }

        var modulePayloads = new List<string>();
        var binaries = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        var dataBlocks = new Dictionary<string, EmbeddedDataBlock>(StringComparer.OrdinalIgnoreCase);
        EmbeddedThemeInfo? theme = null;

        foreach (var frame in id3Tag.GetFrames<UserTextInformationFrame>())
        {
            var description = Normalize(frame.Description);
            var payload = JoinFrameText(frame.Text);
            if (string.IsNullOrWhiteSpace(description) || string.IsNullOrWhiteSpace(payload))
            {
                continue;
            }

            if (IsThemeDescription(description))
            {
                theme ??= TryParseThemeFrame(payload);
                continue;
            }

            if (description.StartsWith(ModulePrefix, StringComparison.OrdinalIgnoreCase))
            {
                modulePayloads.Add(payload);
                continue;
            }

            if (description.StartsWith(BinaryPrefix, StringComparison.OrdinalIgnoreCase))
            {
                var id = description[BinaryPrefix.Length..].Trim();
                if (!string.IsNullOrWhiteSpace(id) && TryDecodeBinary(payload, out var bytes))
                {
                    binaries[id] = bytes;
                }

                continue;
            }

            if (description.StartsWith(DataPrefix, StringComparison.OrdinalIgnoreCase))
            {
                var id = description[DataPrefix.Length..].Trim();
                if (!string.IsNullOrWhiteSpace(id))
                {
                    dataBlocks[id] = new EmbeddedDataBlock(id, payload, TryParseJson(payload));
                }
            }
        }

        var set = BuildFromModules(modulePayloads, binaries, dataBlocks);
        return theme is not null ? set with { Theme = theme } : set;
    }

    public static EmbeddedModuleSet ReadFromPackageVisualizers(
        IEnumerable<object> visualizers,
        Func<string, byte[]?> readEntry)
    {
        var elements = visualizers
            .OfType<JsonElement>()
            .Where(static element => element.ValueKind == JsonValueKind.Object);
        return ReadFromPackageVisualizers(elements, readEntry);
    }

    public static EmbeddedModuleSet ReadFromPackageVisualizers(
        IEnumerable<JsonElement> visualizers,
        Func<string, byte[]?> readEntry)
    {
        EmbeddedVisualizerContext? visualizer = null;
        EmbeddedHtmlContext? html = null;
        EmbeddedMarkdownContext? markdown = null;
        EmbeddedVideoContext? video = null;

        foreach (var element in visualizers)
        {
            if (!TryGetJsonString(element, "type", out var type))
            {
                continue;
            }

            var runtime = TryGetJsonString(element, "runtime", out var runtimeValue)
                ? runtimeValue
                : type;
            TryGetJsonString(element, "id", out var id);
            TryGetJsonString(element, "version", out var version);
            TryGetJsonString(element, "binaryEntry", out var binaryEntry);
            TryGetJsonString(element, "moduleEntry", out var moduleEntry);

            var modulePayload = TryReadTextEntry(moduleEntry, readEntry);
            if (string.IsNullOrWhiteSpace(modulePayload))
            {
                modulePayload = BuildModulePayloadFromDescriptor(element, type, runtime, id, binaryEntry, version);
            }

            if (string.IsNullOrWhiteSpace(modulePayload) || string.IsNullOrWhiteSpace(binaryEntry))
            {
                continue;
            }

            var binary = readEntry(binaryEntry);
            if (binary is not { Length: > 0 })
            {
                continue;
            }

            var binaryAssets = ReadBinaryAssets(element, readEntry);
            var textPayloads = ReadTextAssets(element, readEntry);
            var dataBlocks = textPayloads.ToDictionary(
                static item => item.Key,
                static item => new EmbeddedDataBlock(item.Key, item.Value, TryParseJson(item.Value)),
                StringComparer.OrdinalIgnoreCase);

            var module = TryParseModule(modulePayload);
            if (module is null)
            {
                continue;
            }

            dataBlocks = MergeDataBlocks(module, dataBlocks);

            switch (type.ToLowerInvariant())
            {
                case "visualizer" when string.Equals(runtime, "wasm", StringComparison.OrdinalIgnoreCase):
                    visualizer = new EmbeddedVisualizerContext(
                        module,
                        binary,
                        dataBlocks,
                        TryReadDisplayName(module, dataBlocks));
                    break;
                case "html":
                    html = new EmbeddedHtmlContext(
                        string.IsNullOrWhiteSpace(id) ? module.Id : id,
                        binary,
                        MergeBinaryAssets(module, binaryAssets),
                        MergeTextAssets(module, textPayloads),
                        string.IsNullOrWhiteSpace(version) ? module.Version : version);
                    break;
                case "markdown":
                    markdown = new EmbeddedMarkdownContext(
                        string.IsNullOrWhiteSpace(id) ? module.Id : id,
                        binary,
                        GetOptionalDataBlock(module, "style", dataBlocks),
                        string.IsNullOrWhiteSpace(version) ? module.Version : version);
                    break;
                case "video":
                    video = new EmbeddedVideoContext(
                        string.IsNullOrWhiteSpace(id) ? module.Id : id,
                        runtime,
                        binary,
                        module.Width,
                        module.Height,
                        module.Autoplay,
                        module.Loop,
                        string.IsNullOrWhiteSpace(version) ? module.Version : version);
                    break;
            }
        }

        return new EmbeddedModuleSet(visualizer, html, markdown, video);
    }

    private static EmbeddedModuleSet BuildFromModules(
        IEnumerable<string> modulePayloads,
        IReadOnlyDictionary<string, byte[]> binaries,
        IReadOnlyDictionary<string, EmbeddedDataBlock> dataBlocks)
    {
        EmbeddedVisualizerContext? visualizer = null;
        EmbeddedHtmlContext? html = null;
        EmbeddedMarkdownContext? markdown = null;
        EmbeddedVideoContext? video = null;

        foreach (var payload in modulePayloads)
        {
            var module = TryParseModule(payload);
            if (module is null || !binaries.TryGetValue(module.BinaryRef, out var binary))
            {
                continue;
            }

            switch (module.Type.ToLowerInvariant())
            {
                case "visualizer" when string.Equals(module.Runtime, "wasm", StringComparison.OrdinalIgnoreCase):
                    visualizer = new EmbeddedVisualizerContext(
                        module,
                        binary,
                        new Dictionary<string, EmbeddedDataBlock>(dataBlocks, StringComparer.OrdinalIgnoreCase),
                        TryReadDisplayName(module, dataBlocks));
                    break;
                case "html" when string.Equals(module.Runtime, "html", StringComparison.OrdinalIgnoreCase):
                    html = new EmbeddedHtmlContext(
                        module.Id,
                        binary,
                        ResolveBinaryAssetRefs(module, binaries),
                        ResolveTextDataRefs(module, dataBlocks),
                        module.Version);
                    break;
                case "markdown":
                    markdown = new EmbeddedMarkdownContext(
                        module.Id,
                        binary,
                        GetOptionalDataBlock(module, "style", dataBlocks),
                        module.Version);
                    break;
                case "video":
                    video = new EmbeddedVideoContext(
                        module.Id,
                        module.Runtime,
                        binary,
                        module.Width,
                        module.Height,
                        module.Autoplay,
                        module.Loop,
                        module.Version);
                    break;
            }
        }

        return new EmbeddedModuleSet(visualizer, html, markdown, video);
    }

    private static EmbeddedModuleInfo? TryParseModule(string payload)
    {
        if (TryParseJson(payload) is not JsonObject jsonObject)
        {
            return null;
        }

        var id = ReadRequiredString(jsonObject, "id");
        var type = ReadRequiredString(jsonObject, "type");
        var runtime = ReadRequiredString(jsonObject, "runtime");
        var entry = ReadOptionalString(jsonObject, "entry");
        var binaryRef = ReadRequiredString(jsonObject, "binaryRef");
        var isVisualizer = string.Equals(type, "visualizer", StringComparison.OrdinalIgnoreCase);

        if (string.IsNullOrWhiteSpace(id) ||
            string.IsNullOrWhiteSpace(type) ||
            string.IsNullOrWhiteSpace(runtime) ||
            string.IsNullOrWhiteSpace(binaryRef) ||
            (isVisualizer && string.IsNullOrWhiteSpace(entry)))
        {
            return null;
        }

        return new EmbeddedModuleInfo(
            id,
            type,
            runtime,
            entry ?? string.Empty,
            ReadDataRefs(jsonObject["dataRefs"]),
            binaryRef,
            ReadOptionalString(jsonObject, "version"),
            TryReadInteger(jsonObject, "width"),
            TryReadInteger(jsonObject, "height"),
            TryReadBoolean(jsonObject, "autoplay", false),
            TryReadBoolean(jsonObject, "loop", true));
    }

    private static string? BuildModulePayloadFromDescriptor(
        JsonElement element,
        string type,
        string runtime,
        string id,
        string binaryEntry,
        string version)
    {
        if (string.IsNullOrWhiteSpace(type) || string.IsNullOrWhiteSpace(binaryEntry))
        {
            return null;
        }

        var binaryRef = Path.GetFileNameWithoutExtension(binaryEntry);
        var payload = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["id"] = string.IsNullOrWhiteSpace(id) ? binaryRef : id,
            ["type"] = type,
            ["runtime"] = runtime,
            ["entry"] = TryGetJsonString(element, "entry", out var entry) ? entry : "_start",
            ["binaryRef"] = binaryRef,
            ["version"] = string.IsNullOrWhiteSpace(version) ? null : version,
            ["dataRefs"] = ReadAssetReferenceNames(element, "dataAssets"),
            ["width"] = TryGetJsonInt(element, "width"),
            ["height"] = TryGetJsonInt(element, "height"),
            ["autoplay"] = TryGetJsonBool(element, "autoplay", false),
            ["loop"] = TryGetJsonBool(element, "loop", true),
        };

        return JsonSerializer.Serialize(payload);
    }

    private static IReadOnlyDictionary<string, string> ReadDataRefs(JsonNode? node)
    {
        if (node is not JsonObject obj)
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        var refs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var property in obj)
        {
            if (property.Value is JsonValue value &&
                value.TryGetValue<string>(out var text) &&
                !string.IsNullOrWhiteSpace(property.Key) &&
                !string.IsNullOrWhiteSpace(text))
            {
                refs[property.Key] = text.Trim();
            }
        }

        return refs;
    }

    private static IReadOnlyDictionary<string, string> ReadAssetReferenceNames(JsonElement element, string propertyName)
    {
        var refs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!element.TryGetProperty(propertyName, out var assets) || assets.ValueKind != JsonValueKind.Object)
        {
            return refs;
        }

        foreach (var prop in assets.EnumerateObject())
        {
            if (prop.Value.ValueKind == JsonValueKind.String)
            {
                refs[prop.Name] = prop.Name;
            }
        }

        return refs;
    }

    private static IReadOnlyDictionary<string, byte[]> ReadBinaryAssets(JsonElement element, Func<string, byte[]?> readEntry)
    {
        var assets = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        if (!element.TryGetProperty("binaryAssets", out var binaryAssets) || binaryAssets.ValueKind != JsonValueKind.Object)
        {
            return assets;
        }

        foreach (var prop in binaryAssets.EnumerateObject())
        {
            if (prop.Value.ValueKind != JsonValueKind.String)
            {
                continue;
            }

            var entry = prop.Value.GetString();
            if (string.IsNullOrWhiteSpace(entry))
            {
                continue;
            }

            var bytes = readEntry(entry);
            if (bytes is { Length: > 0 })
            {
                assets[prop.Name] = bytes;
                assets[Path.GetFileNameWithoutExtension(entry)] = bytes;
            }
        }

        return assets;
    }

    private static IReadOnlyDictionary<string, string> ReadTextAssets(JsonElement element, Func<string, byte[]?> readEntry)
    {
        var assets = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!element.TryGetProperty("dataAssets", out var dataAssets) || dataAssets.ValueKind != JsonValueKind.Object)
        {
            return assets;
        }

        foreach (var prop in dataAssets.EnumerateObject())
        {
            if (prop.Value.ValueKind != JsonValueKind.String)
            {
                continue;
            }

            var entry = prop.Value.GetString();
            if (string.IsNullOrWhiteSpace(entry))
            {
                continue;
            }

            var bytes = readEntry(entry);
            if (bytes is { Length: > 0 })
            {
                assets[prop.Name] = Encoding.UTF8.GetString(bytes);
            }
        }

        return assets;
    }

    private static IReadOnlyDictionary<string, byte[]> MergeBinaryAssets(
        EmbeddedModuleInfo module,
        IReadOnlyDictionary<string, byte[]> assets)
    {
        var merged = new Dictionary<string, byte[]>(assets, StringComparer.OrdinalIgnoreCase);
        foreach (var (bindingName, id) in module.DataRefs)
        {
            if (assets.TryGetValue(bindingName, out var bytes))
            {
                merged[id] = bytes;
            }
        }

        return merged;
    }

    private static IReadOnlyDictionary<string, string> MergeTextAssets(
        EmbeddedModuleInfo module,
        IReadOnlyDictionary<string, string> assets)
    {
        var merged = new Dictionary<string, string>(assets, StringComparer.OrdinalIgnoreCase);
        foreach (var (bindingName, id) in module.DataRefs)
        {
            if (assets.TryGetValue(bindingName, out var text))
            {
                merged[id] = text;
            }
        }

        return merged;
    }

    private static Dictionary<string, EmbeddedDataBlock> MergeDataBlocks(
        EmbeddedModuleInfo module,
        IReadOnlyDictionary<string, EmbeddedDataBlock> dataBlocks)
    {
        var merged = new Dictionary<string, EmbeddedDataBlock>(dataBlocks, StringComparer.OrdinalIgnoreCase);
        foreach (var (bindingName, id) in module.DataRefs)
        {
            if (dataBlocks.TryGetValue(bindingName, out var block))
            {
                merged[id] = new EmbeddedDataBlock(id, block.RawText, block.JsonValue?.DeepClone());
            }
        }

        return merged;
    }

    private static IReadOnlyDictionary<string, byte[]> ResolveBinaryAssetRefs(
        EmbeddedModuleInfo module,
        IReadOnlyDictionary<string, byte[]> binaries)
    {
        var assets = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        foreach (var (bindingName, binaryId) in module.DataRefs)
        {
            if (binaries.TryGetValue(binaryId, out var bytes))
            {
                assets[bindingName] = bytes;
                assets[binaryId] = bytes;
            }
        }

        return assets;
    }

    private static IReadOnlyDictionary<string, string> ResolveTextDataRefs(
        EmbeddedModuleInfo module,
        IReadOnlyDictionary<string, EmbeddedDataBlock> dataBlocks)
    {
        var assets = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (bindingName, dataId) in module.DataRefs)
        {
            if (dataBlocks.TryGetValue(dataId, out var block))
            {
                assets[bindingName] = block.RawText;
                assets[dataId] = block.RawText;
            }
        }

        return assets;
    }

    private static string? GetOptionalDataBlock(
        EmbeddedModuleInfo module,
        string bindingName,
        IReadOnlyDictionary<string, EmbeddedDataBlock> dataBlocks)
    {
        return module.DataRefs.TryGetValue(bindingName, out var dataBlockId) &&
            dataBlocks.TryGetValue(dataBlockId, out var dataBlock)
                ? dataBlock.RawText
                : null;
    }

    private static string? TryReadDisplayName(
        EmbeddedModuleInfo module,
        IReadOnlyDictionary<string, EmbeddedDataBlock> dataBlocks)
    {
        foreach (var bindingName in new[] { "config", "metadata" })
        {
            if (!module.DataRefs.TryGetValue(bindingName, out var dataBlockId) ||
                !dataBlocks.TryGetValue(dataBlockId, out var block))
            {
                continue;
            }

            var name = block.TryGetString("name", "displayName", "title");
            if (!string.IsNullOrWhiteSpace(name))
            {
                return name;
            }
        }

        return null;
    }

    private static string? TryReadTextEntry(string entry, Func<string, byte[]?> readEntry)
    {
        if (string.IsNullOrWhiteSpace(entry))
        {
            return null;
        }

        var bytes = readEntry(entry);
        return bytes is { Length: > 0 } ? Encoding.UTF8.GetString(bytes) : null;
    }

    private static bool TryGetJsonString(JsonElement element, string propertyName, out string value)
    {
        value = string.Empty;
        if (element.TryGetProperty(propertyName, out var prop) && prop.ValueKind == JsonValueKind.String)
        {
            value = prop.GetString() ?? string.Empty;
            return !string.IsNullOrWhiteSpace(value);
        }

        return false;
    }

    private static int? TryGetJsonInt(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var prop) && prop.ValueKind == JsonValueKind.Number && prop.TryGetInt32(out var value)
            ? value
            : null;

    private static bool TryGetJsonBool(JsonElement element, string propertyName, bool defaultValue) =>
        element.TryGetProperty(propertyName, out var prop) && prop.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? prop.GetBoolean()
            : defaultValue;

    private static string? ReadOptionalString(JsonObject obj, string propertyName)
    {
        return obj[propertyName] is JsonValue value &&
            value.TryGetValue<string>(out var text) &&
            !string.IsNullOrWhiteSpace(text)
                ? text.Trim()
                : null;
    }

    private static string? ReadRequiredString(JsonObject obj, string propertyName) => ReadOptionalString(obj, propertyName);

    private static int? TryReadInteger(JsonObject obj, string propertyName)
    {
        return obj[propertyName] is JsonValue value && value.TryGetValue<int>(out var integer)
            ? integer
            : null;
    }

    private static bool TryReadBoolean(JsonObject obj, string propertyName, bool defaultValue)
    {
        return obj[propertyName] is JsonValue value && value.TryGetValue<bool>(out var boolean)
            ? boolean
            : defaultValue;
    }

    private static JsonNode? TryParseJson(string value)
    {
        try
        {
            return JsonNode.Parse(value);
        }
        catch
        {
            return null;
        }
    }

    private static bool TryDecodeBinary(string payload, out byte[] bytes)
    {
        try
        {
            bytes = Convert.FromBase64String(payload.Trim());
            return bytes.Length > 0;
        }
        catch
        {
            bytes = [];
            return false;
        }
    }

    private static string? JoinFrameText(string[]? values)
    {
        if (values is null || values.Length == 0)
        {
            return null;
        }

        var normalized = values
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Select(static value => value.Trim())
            .ToArray();

        return normalized.Length == 0 ? null : string.Join(Environment.NewLine, normalized);
    }

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private const string ThemeFrameKey = "DELTA_THEME";
    private const string ThemeFramePrefix = "DELTA_THEME_";

    private static bool IsThemeDescription(string description) =>
        string.Equals(description, ThemeFrameKey, StringComparison.OrdinalIgnoreCase) ||
        description.StartsWith(ThemeFramePrefix, StringComparison.OrdinalIgnoreCase);

    private static EmbeddedThemeInfo? TryParseThemeFrame(string payload)
    {
        if (TryParseJson(payload) is not JsonObject obj)
            return null;

        var mode = TryGetStringFromKeys(obj, "mode", "themeMode");
        var accent = TryGetStringFromKeys(obj, "accent", "themeAccent");

        if (string.IsNullOrWhiteSpace(mode) || string.IsNullOrWhiteSpace(accent))
            return null;

        return new EmbeddedThemeInfo(mode.Trim(), accent.Trim());
    }

    private static string? TryGetStringFromKeys(JsonObject obj, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (obj[key] is JsonValue v && v.TryGetValue<string>(out var s) && !string.IsNullOrWhiteSpace(s))
                return s.Trim();
        }
        return null;
    }
}
