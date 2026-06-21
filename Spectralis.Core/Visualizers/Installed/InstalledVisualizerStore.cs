using System.Text.Json;

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
            var textAssets = (IReadOnlyDictionary<string, string>)(record.DataBlocks
                ?? new Dictionary<string, string>());

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
