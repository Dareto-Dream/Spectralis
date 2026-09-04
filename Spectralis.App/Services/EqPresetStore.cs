using System.Text.Json;
using System.Text.Json.Serialization;
using Spectralis.Core.Audio.Effects;

namespace Spectralis.App.Services;

/// <summary>
/// Persists user-saved parametric-EQ presets as JSON alongside the other app
/// settings (<c>%AppData%/Spectralis/eq-presets.json</c>). Built-in presets
/// (<see cref="EqPresets.BuiltIn"/>) are never written here.
/// </summary>
public static class EqPresetStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>Test seam — when set, presets are read/written here instead of the app-data path.</summary>
    public static string? PathOverride { get; set; }

    public static string PresetsPath =>
        PathOverride ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Spectralis",
            "eq-presets.json");

    public static IReadOnlyList<EqPreset> Load()
    {
        try
        {
            if (!File.Exists(PresetsPath))
            {
                return [];
            }

            var json = File.ReadAllText(PresetsPath);
            var presets = JsonSerializer.Deserialize<List<StoredPreset>>(json, SerializerOptions) ?? [];
            return presets
                .Where(p => !string.IsNullOrWhiteSpace(p.Name))
                .Select(p => p.ToPreset())
                .Where(p => !EqPresets.BuiltIn.Any(b => string.Equals(b.Name, p.Name, StringComparison.OrdinalIgnoreCase)))
                .ToList();
        }
        catch
        {
            return [];
        }
    }

    public static void Save(IEnumerable<EqPreset> presets)
    {
        var directory = Path.GetDirectoryName(PresetsPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var stored = presets
            .Where(p => !string.IsNullOrWhiteSpace(p.Name))
            .Select(StoredPreset.From)
            .ToList();
        File.WriteAllText(PresetsPath, JsonSerializer.Serialize(stored, SerializerOptions));
    }

    public static void AddOrReplace(EqPreset preset)
    {
        var list = Load().ToList();
        list.RemoveAll(p => string.Equals(p.Name, preset.Name, StringComparison.OrdinalIgnoreCase));
        list.Add(preset);
        Save(list);
    }

    public static void Delete(string name)
    {
        var list = Load().ToList();
        list.RemoveAll(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
        Save(list);
    }

    private sealed class StoredPreset
    {
        public string Name { get; set; } = string.Empty;
        public float PreampDb { get; set; }
        public float OutputGainDb { get; set; }
        public List<StoredBand> Bands { get; set; } = [];

        public static StoredPreset From(EqPreset p) => new()
        {
            Name = p.Name,
            PreampDb = p.PreampDb,
            OutputGainDb = p.OutputGainDb,
            Bands = p.Bands.Select(b => new StoredBand
            {
                Frequency = b.Frequency,
                GainDb = b.GainDb,
                Q = b.Q,
                Type = b.Type,
                Enabled = b.Enabled,
            }).ToList(),
        };

        public EqPreset ToPreset() => new(
            Name,
            PreampDb,
            OutputGainDb,
            Bands.Select(b => new EqBand(b.Frequency, b.GainDb, b.Q <= 0 ? 1f : b.Q, b.Type, b.Enabled)).ToList());
    }

    private sealed class StoredBand
    {
        public float Frequency { get; set; }
        public float GainDb { get; set; }
        public float Q { get; set; } = 1f;
        public EqFilterType Type { get; set; }
        public bool Enabled { get; set; } = true;
    }
}
