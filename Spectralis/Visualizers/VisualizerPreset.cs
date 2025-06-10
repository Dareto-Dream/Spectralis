using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Spectralis.Visualizers
{
    public class VisualizerPreset
    {
        public string Name { get; set; }
        public string VisualizerName { get; set; }
        public VisualizerSettings Settings { get; set; }
    }

    public class VisualizerPresetStore
    {
        private readonly string _filePath;
        private List<VisualizerPreset> _presets = new List<VisualizerPreset>();

        public IReadOnlyList<VisualizerPreset> Presets => _presets;

        public VisualizerPresetStore(string filePath)
        {
            _filePath = filePath;
            Load();
        }

        public void Add(VisualizerPreset preset)
        {
            _presets.RemoveAll(p => p.Name == preset.Name);
            _presets.Add(preset);
            Save();
        }

        public void Remove(string name)
        {
            _presets.RemoveAll(p => p.Name == name);
            Save();
        }

        private void Load()
        {
            if (!File.Exists(_filePath)) return;
            try
            {
                var json = File.ReadAllText(_filePath);
                _presets = JsonConvert.DeserializeObject<List<VisualizerPreset>>(json)
                    ?? new List<VisualizerPreset>();
            }
            catch { _presets = new List<VisualizerPreset>(); }
        }

        private void Save()
        {
            File.WriteAllText(_filePath, JsonConvert.SerializeObject(_presets, Formatting.Indented));
        }
    }
}
