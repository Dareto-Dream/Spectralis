using System.Text.Json;
using Spectralis.App.ViewModels;

namespace Spectralis.App.Services;

/// <summary>Persists named spin-wheel entry sets so they can be reloaded later.</summary>
public static class SavedWheelStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
    };

    private static string StorePath =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Spectralis",
            "wheels.json");

    public static List<SavedWheel> LoadAll()
    {
        try
        {
            if (!File.Exists(StorePath)) return [];
            var json = File.ReadAllText(StorePath);
            return JsonSerializer.Deserialize<List<SavedWheel>>(json, SerializerOptions) ?? [];
        }
        catch
        {
            return [];
        }
    }

    /// <summary>Saves (or overwrites, by case-insensitive name match) a named wheel.</summary>
    public static void Save(string name, IEnumerable<WheelEntry> entries)
    {
        var all = LoadAll();
        var snapshot = entries.Select(e => e.Clone()).ToList();
        var existing = all.FirstOrDefault(w => string.Equals(w.Name, name, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
            existing.Entries = snapshot;
        else
            all.Add(new SavedWheel { Name = name, Entries = snapshot });

        Persist(all);
    }

    public static void Delete(string name)
    {
        var all = LoadAll();
        if (all.RemoveAll(w => string.Equals(w.Name, name, StringComparison.OrdinalIgnoreCase)) > 0)
            Persist(all);
    }

    private static void Persist(List<SavedWheel> wheels)
    {
        try
        {
            var dir = Path.GetDirectoryName(StorePath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            File.WriteAllText(StorePath, JsonSerializer.Serialize(wheels, SerializerOptions));
        }
        catch { }
    }
}
