using System.Text.Json;
using Spectralis.Core.Audio.Effects;

namespace Spectralis.App.Services;

/// <summary>A named, saved whole-rack snapshot (order, enable state, every parameter).</summary>
public sealed record EffectChainPreset(string Name, string ChainJson)
{
    public void ApplyTo(EffectChain chain) => EffectChainState.Restore(chain, ChainJson);

    public static EffectChainPreset FromChain(EffectChain chain, string name) =>
        new(name, EffectChainState.Serialize(chain));
}

/// <summary>
/// Persists user-saved effect-chain presets as JSON alongside the other app
/// settings (<c>%AppData%/Spectralis/effect-chain-presets.json</c>). Built-in
/// presets (<see cref="EffectChainPresets.BuiltInNames"/>) are never written here.
/// </summary>
public static class EffectChainPresetStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        WriteIndented = true,
    };

    /// <summary>Test seam — when set, presets are read/written here instead of the app-data path.</summary>
    public static string? PathOverride { get; set; }

    public static string PresetsPath =>
        PathOverride ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Spectralis",
            "effect-chain-presets.json");

    public static IReadOnlyList<EffectChainPreset> Load()
    {
        try
        {
            if (!File.Exists(PresetsPath))
            {
                return [];
            }

            var json = File.ReadAllText(PresetsPath);
            var presets = JsonSerializer.Deserialize<List<EffectChainPreset>>(json, SerializerOptions) ?? [];
            return presets
                .Where(p => !string.IsNullOrWhiteSpace(p.Name))
                .Where(p => !EffectChainPresets.BuiltInNames.Any(b => string.Equals(b, p.Name, StringComparison.OrdinalIgnoreCase)))
                .ToList();
        }
        catch
        {
            return [];
        }
    }

    public static void Save(IEnumerable<EffectChainPreset> presets)
    {
        var directory = Path.GetDirectoryName(PresetsPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var stored = presets.Where(p => !string.IsNullOrWhiteSpace(p.Name)).ToList();
        File.WriteAllText(PresetsPath, JsonSerializer.Serialize(stored, SerializerOptions));
    }

    public static void AddOrReplace(EffectChainPreset preset)
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
}
