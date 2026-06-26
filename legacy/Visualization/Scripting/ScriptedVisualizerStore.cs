using System.IO;
using System.Text.Json;

namespace Spectralis;

internal static class ScriptedVisualizerStore
{
    private static readonly string Dir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Spectralis", "scripts");

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        AllowTrailingCommas = true,
    };

    public static List<ScriptedVisualizerDefinition> LoadAll()
    {
        var result = new List<ScriptedVisualizerDefinition>();
        if (!Directory.Exists(Dir)) return result;
        foreach (var file in Directory.GetFiles(Dir, "*.json"))
        {
            try
            {
                var text = File.ReadAllText(file);
                var def = JsonSerializer.Deserialize<ScriptedVisualizerDefinition>(text, JsonOpts);
                if (def is not null) result.Add(def);
            }
            catch { }
        }
        return result.OrderBy(d => d.CreatedAt).ToList();
    }

    public static bool TryGet(string id, out ScriptedVisualizerDefinition def)
    {
        var path = FilePath(id);
        if (!File.Exists(path)) { def = null!; return false; }
        try
        {
            var text = File.ReadAllText(path);
            def = JsonSerializer.Deserialize<ScriptedVisualizerDefinition>(text, JsonOpts)!;
            return def is not null;
        }
        catch { def = null!; return false; }
    }

    public static void Save(ScriptedVisualizerDefinition def)
    {
        Directory.CreateDirectory(Dir);
        File.WriteAllText(FilePath(def.Id), JsonSerializer.Serialize(def, JsonOpts));
    }

    public static void Delete(string id)
    {
        var path = FilePath(id);
        if (File.Exists(path)) File.Delete(path);
    }

    private static string FilePath(string id) => Path.Combine(Dir, $"{id}.json");
}
