using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;

namespace Spectralis.Core.SharedPlay
{
    public class SessionHistoryStore
    {
        private static readonly JsonSerializerOptions _json = new() { WriteIndented = true };
        private readonly string _path;

        public SessionHistoryStore(string path) => _path = path;

        public async Task<List<SharedPlayStats>> LoadAsync()
        {
            if (!File.Exists(_path)) return new();
            try
            {
                string json = await File.ReadAllTextAsync(_path);
                return JsonSerializer.Deserialize<List<SharedPlayStats>>(json, _json) ?? new();
            }
            catch { return new(); }
        }

        public async Task AppendAsync(SharedPlayStats stats)
        {
            var history = await LoadAsync();
            history.Add(stats);
            if (history.Count > 50) history.RemoveAt(0);
            string tmp = _path + ".tmp";
            await File.WriteAllTextAsync(tmp, JsonSerializer.Serialize(history, _json));
            if (File.Exists(_path)) File.Delete(_path);
            File.Move(tmp, _path);
        }
    }
}
