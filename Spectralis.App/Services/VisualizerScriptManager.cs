using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Spectralis.Core.Visualizers;

namespace Spectralis.App.Services
{
    public sealed class ScriptVisualizerEntry
    {
        public string Id { get; set; } = "";
        public string DisplayName { get; set; } = "";
        public string FilePath { get; set; } = "";
    }

    public sealed class VisualizerScriptManager
    {
        private readonly string _scriptDir;
        private readonly string _indexPath;
        private readonly List<ScriptVisualizerEntry> _entries = new();

        public IReadOnlyList<ScriptVisualizerEntry> Entries => _entries;

        public VisualizerScriptManager(string scriptDir)
        {
            _scriptDir = scriptDir;
            _indexPath = Path.Combine(scriptDir, "index.json");
            Directory.CreateDirectory(scriptDir);
            Load();
        }

        private void Load()
        {
            if (!File.Exists(_indexPath)) return;
            try
            {
                var json = File.ReadAllText(_indexPath);
                var loaded = JsonSerializer.Deserialize<List<ScriptVisualizerEntry>>(json);
                if (loaded != null) _entries.AddRange(loaded);
            }
            catch { }
        }

        private void Save()
        {
            var json = JsonSerializer.Serialize(_entries, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_indexPath, json);
        }

        public void Install(string displayName, string sourceFile)
        {
            string id = "script:" + Guid.NewGuid().ToString("N")[..8];
            string dest = Path.Combine(_scriptDir, Path.GetFileName(sourceFile));
            File.Copy(sourceFile, dest, overwrite: true);
            _entries.Add(new ScriptVisualizerEntry { Id = id, DisplayName = displayName, FilePath = dest });
            Save();
        }

        public void Remove(string id)
        {
            var entry = _entries.FirstOrDefault(e => e.Id == id);
            if (entry == null) return;
            if (File.Exists(entry.FilePath)) File.Delete(entry.FilePath);
            _entries.Remove(entry);
            Save();
        }

        public string? ReadSource(string id)
        {
            var entry = _entries.FirstOrDefault(e => e.Id == id);
            if (entry == null || !File.Exists(entry.FilePath)) return null;
            return File.ReadAllText(entry.FilePath);
        }
    }
}
