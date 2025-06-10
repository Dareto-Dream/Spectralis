using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;

namespace Spectralis.Streaming
{
    public class StreamingHistoryStore
    {
        private readonly string _path;
        private List<StreamingHistoryEntry> _entries = new List<StreamingHistoryEntry>();
        private const int MaxEntries = 200;

        public IReadOnlyList<StreamingHistoryEntry> Entries => _entries;

        public StreamingHistoryStore(string path)
        {
            _path = path;
            Load();
        }

        public void Add(StreamingTrack track)
        {
            _entries.Insert(0, new StreamingHistoryEntry
            {
                Track = track,
                PlayedAt = DateTime.UtcNow
            });

            if (_entries.Count > MaxEntries)
                _entries.RemoveRange(MaxEntries, _entries.Count - MaxEntries);

            Save();
        }

        public void Clear()
        {
            _entries.Clear();
            Save();
        }

        private void Load()
        {
            if (!File.Exists(_path)) return;
            try { _entries = JsonConvert.DeserializeObject<List<StreamingHistoryEntry>>(File.ReadAllText(_path)) ?? new List<StreamingHistoryEntry>(); }
            catch { _entries = new List<StreamingHistoryEntry>(); }
        }

        private void Save()
        {
            File.WriteAllText(_path, JsonConvert.SerializeObject(_entries, Formatting.Indented));
        }
    }

    public class StreamingHistoryEntry
    {
        public StreamingTrack Track { get; set; }
        public DateTime PlayedAt { get; set; }
    }
}
