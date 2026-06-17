using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;

namespace Spectralis.Core.Capsule
{
    public class CapsuleCache
    {
        private static readonly JsonSerializerOptions _json = new() { WriteIndented = true };
        private readonly string _cacheDir;
        private Dictionary<string, CapsuleCacheEntry> _entries = new();

        public CapsuleCache(string cacheDir)
        {
            _cacheDir = cacheDir;
            Directory.CreateDirectory(cacheDir);
        }

        public async Task LoadAsync()
        {
            string idx = Path.Combine(_cacheDir, "index.json");
            if (!File.Exists(idx)) return;
            try
            {
                string json = await File.ReadAllTextAsync(idx);
                _entries = JsonSerializer.Deserialize<Dictionary<string, CapsuleCacheEntry>>(json, _json) ?? new();
            }
            catch { _entries = new(); }
        }

        public bool IsStale(string capsulePath)
        {
            string key = Path.GetFileName(capsulePath);
            if (!_entries.TryGetValue(key, out var entry)) return true;
            var modified = File.Exists(capsulePath) ? File.GetLastWriteTimeUtc(capsulePath) : DateTime.MinValue;
            return modified > entry.LastIndexed || DateTimeOffset.UtcNow - entry.LastIndexed > TimeSpan.FromDays(30);
        }

        public async Task UpdateAsync(string capsulePath, CapsuleManifest manifest)
        {
            string key = Path.GetFileName(capsulePath);
            _entries[key] = new CapsuleCacheEntry
            {
                CapsuleId = manifest.Id,
                Title = manifest.Title,
                Artist = manifest.Artist,
                LastIndexed = DateTimeOffset.UtcNow
            };
            await SaveIndexAsync();
        }

        private async Task SaveIndexAsync()
        {
            string idx = Path.Combine(_cacheDir, "index.json");
            string tmp = idx + ".tmp";
            await File.WriteAllTextAsync(tmp, JsonSerializer.Serialize(_entries, _json));
            if (File.Exists(idx)) File.Delete(idx);
            File.Move(tmp, idx);
        }

        public class CapsuleCacheEntry
        {
            public string CapsuleId { get; set; } = string.Empty;
            public string Title { get; set; } = string.Empty;
            public string Artist { get; set; } = string.Empty;
            public DateTimeOffset LastIndexed { get; set; }
        }
    }
}
