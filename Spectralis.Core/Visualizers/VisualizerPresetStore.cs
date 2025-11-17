using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;

namespace Spectralis.Core.Visualizers
{
    public class VisualizerPreset
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Name { get; set; } = string.Empty;
        public string VisualizerId { get; set; } = string.Empty;
        public Dictionary<string, object?> Parameters { get; set; } = new();
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }

    public class VisualizerPresetStore
    {
        private readonly string _filePath;
        private static readonly JsonSerializerOptions _opts = new() { WriteIndented = true };

        public VisualizerPresetStore(string filePath) => _filePath = filePath;

        public async Task<List<VisualizerPreset>> LoadAsync()
        {
            if (!File.Exists(_filePath)) return new();
            try
            {
                string json = await File.ReadAllTextAsync(_filePath);
                return JsonSerializer.Deserialize<List<VisualizerPreset>>(json, _opts) ?? new();
            }
            catch { return new(); }
        }

        public async Task SaveAsync(List<VisualizerPreset> presets)
        {
            string dir = Path.GetDirectoryName(_filePath)!;
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            await File.WriteAllTextAsync(_filePath, JsonSerializer.Serialize(presets, _opts));
        }

        public async Task UpsertAsync(VisualizerPreset preset)
        {
            var list = await LoadAsync();
            int idx = list.FindIndex(p => p.Id == preset.Id);
            if (idx >= 0) list[idx] = preset;
            else list.Add(preset);
            await SaveAsync(list);
        }

        public async Task DeleteAsync(string id)
        {
            var list = await LoadAsync();
            list.RemoveAll(p => p.Id == id);
            await SaveAsync(list);
        }
    }
}
