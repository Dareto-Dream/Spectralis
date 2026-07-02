using System.Text.Json;
using System.Text.Json.Nodes;

namespace Spectralis.Core.Visualizers.Installed;

public sealed class InstalledVisualizerStore
{
    private const string InstalledFileName = "installed.json";
    private const int MaxInstalledVisualizers = 64;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        AllowTrailingCommas = true,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        WriteIndented = true,
    };

    private static string RootPath =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Spectralis",
            "visualizers");

    public IReadOnlyList<InstalledVisualizerDefinition> LoadAll()
    {
        try
        {
            if (!Directory.Exists(RootPath))
                return [];

            return Directory
                .EnumerateFiles(RootPath, InstalledFileName, SearchOption.AllDirectories)
                .Take(MaxInstalledVisualizers)
                .Select(TryLoad)
                .Where(static d => d is not null)
                .Select(static d => d!)
                .OrderBy(static d => d.DisplayName, StringComparer.CurrentCultureIgnoreCase)
                .ToArray();
        }
        catch
        {
            return [];
        }
    }

    public InstalledVisualizerDefinition Install(RedeemableVisualizerPackage package)
    {
        var record = InstalledRecord.FromPackage(package);
        var directory = Path.Combine(RootPath, SanitizePathSegment(record.Id));
        Directory.CreateDirectory(directory);

        var targetPath = Path.Combine(directory, InstalledFileName);
        var tempPath = targetPath + ".tmp";
        File.WriteAllText(tempPath, JsonSerializer.Serialize(record, JsonOptions));

        if (File.Exists(targetPath))
            File.Delete(targetPath);

        File.Move(tempPath, targetPath);

        return new InstalledVisualizerDefinition(
            record.Id,
            record.DisplayName,
            record.Version,
            record.InstalledAtUtc);
    }

    /// <summary>
    /// Loads the full binary content of an installed visualizer by its id.
    /// Returns null if the id is unknown or the stored data is corrupt.
    /// </summary>
    public InstalledVisualizerContent? LoadContent(string id)
    {
        if (string.IsNullOrWhiteSpace(id)) return null;
        var directory = Path.Combine(RootPath, SanitizePathSegment(id));
        var path = Path.Combine(directory, InstalledFileName);
        if (!File.Exists(path)) return null;

        try
        {
            var record = JsonSerializer.Deserialize<InstalledRecord>(File.ReadAllText(path), JsonOptions);
            if (record is null || string.IsNullOrWhiteSpace(record.Id)) return null;

            var htmlBytes = Convert.FromBase64String(record.BinaryBase64);
            var binaryAssets = (record.BinaryAssetsBase64 ?? new Dictionary<string, string>())
                .ToDictionary(
                    static kv => kv.Key,
                    static kv => Convert.FromBase64String(kv.Value),
                    StringComparer.OrdinalIgnoreCase);
            var textAssets = new Dictionary<string, string>(record.DataBlocks
                ?? new Dictionary<string, string>(), StringComparer.OrdinalIgnoreCase);

            // The HTML references assets by the binding name declared in the module's dataRefs
            // (e.g. delta-asset:bgImage), not necessarily by the CDN manifest's literal asset key
            // (e.g. "bg_v2.png"). Without this, delta-asset:/delta-bin: tokens whose binding name
            // differs from the manifest key are left unresolved and images silently fail to load —
            // same dataRefs indirection the ID3-embedded-module path already applies.
            foreach (var (bindingName, assetId) in ParseDataRefs(record.ModuleJson))
            {
                if (binaryAssets.TryGetValue(assetId, out var bytes))
                    binaryAssets.TryAdd(bindingName, bytes);
                if (textAssets.TryGetValue(assetId, out var text))
                    textAssets.TryAdd(bindingName, text);
            }

            return new InstalledVisualizerContent(
                record.Id,
                record.DisplayName,
                record.Version,
                htmlBytes,
                textAssets,
                binaryAssets);
        }
        catch
        {
            return null;
        }
    }

    public void ClearAll()
    {
        if (Directory.Exists(RootPath))
            Directory.Delete(RootPath, recursive: true);
    }

    public int Count()
    {
        if (!Directory.Exists(RootPath)) return 0;
        try
        {
            return Directory
                .EnumerateFiles(RootPath, InstalledFileName, SearchOption.AllDirectories)
                .Take(MaxInstalledVisualizers + 1)
                .Count();
        }
        catch
        {
            return 0;
        }
    }

    private static InstalledVisualizerDefinition? TryLoad(string path)
    {
        try
        {
            var record = JsonSerializer.Deserialize<InstalledRecord>(File.ReadAllText(path), JsonOptions);
            if (record is null || string.IsNullOrWhiteSpace(record.Id) || string.IsNullOrWhiteSpace(record.DisplayName))
                return null;
            return new InstalledVisualizerDefinition(record.Id, record.DisplayName, record.Version, record.InstalledAtUtc);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Reads the bindingName → assetId map from a module's "dataRefs" JSON object, if present.</summary>
    private static IReadOnlyDictionary<string, string> ParseDataRefs(string? moduleJson)
    {
        if (string.IsNullOrWhiteSpace(moduleJson)) return new Dictionary<string, string>();

        try
        {
            if (JsonNode.Parse(moduleJson) is not JsonObject obj || obj["dataRefs"] is not JsonObject dataRefs)
                return new Dictionary<string, string>();

            var refs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var property in dataRefs)
            {
                if (property.Value is JsonValue value &&
                    value.TryGetValue<string>(out var id) &&
                    !string.IsNullOrWhiteSpace(property.Key) &&
                    !string.IsNullOrWhiteSpace(id))
                {
                    refs[property.Key] = id.Trim();
                }
            }

            return refs;
        }
        catch
        {
            return new Dictionary<string, string>();
        }
    }

    private static string SanitizePathSegment(string value)
    {
        var chars = value
            .Trim()
            .Select(static c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.' ? c : '-')
            .ToArray();
        var sanitized = new string(chars).Trim('-', '.', '_');
        return string.IsNullOrWhiteSpace(sanitized) ? "visualizer" : sanitized;
    }

    private sealed record InstalledRecord(
        string Id,
        string DisplayName,
        string? Version,
        string ModuleJson,
        string BinaryBase64,
        Dictionary<string, string>? DataBlocks,
        Dictionary<string, string>? BinaryAssetsBase64,
        string? Source,
        DateTimeOffset InstalledAtUtc)
    {
        public static InstalledRecord FromPackage(RedeemableVisualizerPackage p) =>
            new(
                p.Id.Trim(),
                p.DisplayName.Trim(),
                string.IsNullOrWhiteSpace(p.Version) ? null : p.Version.Trim(),
                p.ModuleJson,
                Convert.ToBase64String(p.Binary),
                new Dictionary<string, string>(p.DataBlocks, StringComparer.OrdinalIgnoreCase),
                p.BinaryAssets.ToDictionary(
                    static kv => kv.Key,
                    static kv => Convert.ToBase64String(kv.Value),
                    StringComparer.OrdinalIgnoreCase),
                p.Source,
                DateTimeOffset.UtcNow);
    }
}
