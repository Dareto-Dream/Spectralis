using System.Text.Json;

namespace Spectralis;

internal sealed class InstalledVisualizerStore
{
    private const string InstalledFileName = "installed.json";
    private const int MaxInstalledVisualizers = 64;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        AllowTrailingCommas = true,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        WriteIndented = true
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
                return Array.Empty<InstalledVisualizerDefinition>();

            return Directory
                .EnumerateFiles(RootPath, InstalledFileName, SearchOption.AllDirectories)
                .Take(MaxInstalledVisualizers)
                .Select(TryLoad)
                .Where(static definition => definition is not null)
                .Select(static definition => definition!)
                .OrderBy(static definition => definition.DisplayName, StringComparer.CurrentCultureIgnoreCase)
                .ToArray();
        }
        catch
        {
            return Array.Empty<InstalledVisualizerDefinition>();
        }
    }

    public InstalledVisualizerDefinition Install(RedeemableVisualizerPackage package)
    {
        var record = InstalledVisualizerRecord.FromPackage(package);
        var directory = Path.Combine(RootPath, SanitizePathSegment(record.Id));
        Directory.CreateDirectory(directory);

        var targetPath = Path.Combine(directory, InstalledFileName);
        var tempPath = Path.Combine(directory, $"{InstalledFileName}.tmp");
        File.WriteAllText(tempPath, JsonSerializer.Serialize(record, JsonOptions));

        if (File.Exists(targetPath))
            File.Delete(targetPath);

        File.Move(tempPath, targetPath);

        return CreateDefinition(record)
            ?? throw new InvalidOperationException("The visualizer was downloaded but could not be loaded.");
    }

    public void ClearAll()
    {
        if (!Directory.Exists(RootPath))
            return;

        Directory.Delete(RootPath, recursive: true);
    }

    private static InstalledVisualizerDefinition? TryLoad(string path)
    {
        try
        {
            var json = File.ReadAllText(path);
            var record = JsonSerializer.Deserialize<InstalledVisualizerRecord>(json, JsonOptions);
            return record is null ? null : CreateDefinition(record);
        }
        catch
        {
            return null;
        }
    }

    private static InstalledVisualizerDefinition? CreateDefinition(InstalledVisualizerRecord record)
    {
        if (string.IsNullOrWhiteSpace(record.Id) ||
            string.IsNullOrWhiteSpace(record.DisplayName) ||
            string.IsNullOrWhiteSpace(record.ModuleJson) ||
            string.IsNullOrWhiteSpace(record.BinaryBase64))
        {
            return null;
        }

        byte[] binary;
        try
        {
            binary = Convert.FromBase64String(record.BinaryBase64);
        }
        catch
        {
            return null;
        }

        if (EmbeddedVisualizerMetadataReader.TryCreateVisualizerContext(
            record.ModuleJson,
            binary,
            record.DataBlocks ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            record.DisplayName) is { } visualizerContext)
        {
            return new InstalledVisualizerDefinition(
                record.Id.Trim(),
                record.DisplayName.Trim(),
                record.Version,
                visualizerContext,
                HtmlContext: null);
        }

        if (EmbeddedVisualizerMetadataReader.TryCreateHtmlContext(
            record.ModuleJson,
            binary,
            DecodeBinaryAssets(record.BinaryAssetsBase64)) is { } htmlContext)
        {
            return new InstalledVisualizerDefinition(
                record.Id.Trim(),
                record.DisplayName.Trim(),
                record.Version,
                Context: null,
                htmlContext);
        }

        return null;
    }

    private static IReadOnlyDictionary<string, byte[]> DecodeBinaryAssets(Dictionary<string, string>? assetsBase64)
    {
        var assets = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        if (assetsBase64 is null)
            return assets;

        foreach (var (id, value) in assetsBase64)
        {
            if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(value))
                continue;

            try
            {
                assets[id.Trim()] = Convert.FromBase64String(value);
            }
            catch
            {
                // Ignore a bad optional asset; the package validator will decide if the module can run.
            }
        }

        return assets;
    }

    private static string SanitizePathSegment(string value)
    {
        var chars = value
            .Trim()
            .Select(static character =>
                char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or '.'
                    ? character
                    : '-')
            .ToArray();

        var sanitized = new string(chars).Trim('-', '.', '_');
        return string.IsNullOrWhiteSpace(sanitized) ? "visualizer" : sanitized;
    }

    private sealed record InstalledVisualizerRecord(
        string Id,
        string DisplayName,
        string? Version,
        string ModuleJson,
        string BinaryBase64,
        Dictionary<string, string> DataBlocks,
        Dictionary<string, string>? BinaryAssetsBase64,
        string Source,
        DateTimeOffset InstalledAtUtc)
    {
        public static InstalledVisualizerRecord FromPackage(RedeemableVisualizerPackage package) =>
            new(
                package.Id.Trim(),
                package.DisplayName.Trim(),
                string.IsNullOrWhiteSpace(package.Version) ? null : package.Version.Trim(),
                package.ModuleJson,
                Convert.ToBase64String(package.Binary),
                new Dictionary<string, string>(package.DataBlocks, StringComparer.OrdinalIgnoreCase),
                package.BinaryAssets.ToDictionary(
                    static item => item.Key,
                    static item => Convert.ToBase64String(item.Value),
                    StringComparer.OrdinalIgnoreCase),
                package.Source,
                DateTimeOffset.UtcNow);
    }
}
