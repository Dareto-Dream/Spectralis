using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;

namespace Spectralis.Core.Analysis
{
    public class CachedAnalysis
    {
        public string FilePath { get; set; } = string.Empty;
        public long FileSizeBytes { get; set; }
        public DateTime AnalyzedAt { get; set; }
        public float Bpm { get; set; }
        public float BpmConfidence { get; set; }
        public string KeyName { get; set; } = string.Empty;
        public float KeyConfidence { get; set; }
        public float LoudnessLufs { get; set; }
    }

    public class AnalysisCache
    {
        private readonly string _filePath;
        private readonly Dictionary<string, CachedAnalysis> _cache = new(StringComparer.OrdinalIgnoreCase);
        private static readonly JsonSerializerOptions _opts = new() { WriteIndented = false };

        public AnalysisCache(string filePath)
        {
            _filePath = filePath;
            Load();
        }

        private void Load()
        {
            if (!File.Exists(_filePath)) return;
            try
            {
                var list = JsonSerializer.Deserialize<List<CachedAnalysis>>(File.ReadAllText(_filePath));
                if (list == null) return;
                foreach (var entry in list)
                    _cache[entry.FilePath] = entry;
            }
            catch { }
        }

        public async Task SaveAsync()
        {
            string dir = Path.GetDirectoryName(_filePath)!;
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            string tmp = _filePath + ".tmp";
            var list = new List<CachedAnalysis>(_cache.Values);
            await File.WriteAllTextAsync(tmp, JsonSerializer.Serialize(list, _opts));
            if (File.Exists(_filePath)) File.Delete(_filePath);
            File.Move(tmp, _filePath);
        }

        public CachedAnalysis? Get(string filePath)
        {
            if (!_cache.TryGetValue(filePath, out var entry)) return null;
            if (!File.Exists(filePath)) return null;
            var info = new FileInfo(filePath);
            if (info.Length != entry.FileSizeBytes) return null;
            return entry;
        }

        public void Store(AnalysisResult result)
        {
            var info = new FileInfo(result.FilePath);
            _cache[result.FilePath] = new CachedAnalysis
            {
                FilePath = result.FilePath,
                FileSizeBytes = info.Exists ? info.Length : 0,
                AnalyzedAt = DateTime.UtcNow,
                Bpm = result.Bpm.Bpm,
                BpmConfidence = result.Bpm.Confidence,
                KeyName = result.Key.Name,
                KeyConfidence = result.Key.Confidence,
                LoudnessLufs = result.LoudnessLufs
            };
        }

        public void Invalidate(string filePath) => _cache.Remove(filePath);
    }
}
