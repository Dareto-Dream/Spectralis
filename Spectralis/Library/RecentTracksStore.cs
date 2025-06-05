using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;

namespace Spectralis.Library
{
    public class RecentTracksStore
    {
        private readonly string _path;
        private List<RecentTrackEntry> _entries = new List<RecentTrackEntry>();
        private const int MaxEntries = 50;

        public IReadOnlyList<RecentTrackEntry> Entries => _entries;

        public RecentTracksStore(string path)
        {
            _path = path;
            Load();
        }

        public void Record(TrackInfo track)
        {
            _entries.RemoveAll(e => e.FilePath == track.FilePath);
            _entries.Insert(0, new RecentTrackEntry
            {
                FilePath = track.FilePath,
                Title = track.Title,
                Artist = track.Artist,
                LastPlayed = DateTime.UtcNow,
                PlayCount = 1 + (_entries.Find(e => e.FilePath == track.FilePath)?.PlayCount ?? 0)
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
            try { _entries = JsonConvert.DeserializeObject<List<RecentTrackEntry>>(File.ReadAllText(_path)) ?? new List<RecentTrackEntry>(); }
            catch { _entries = new List<RecentTrackEntry>(); }
        }

        private void Save()
        {
            File.WriteAllText(_path, JsonConvert.SerializeObject(_entries, Formatting.Indented));
        }
    }

    public class RecentTrackEntry
    {
        public string FilePath { get; set; }
        public string Title { get; set; }
        public string Artist { get; set; }
        public DateTime LastPlayed { get; set; }
        public int PlayCount { get; set; }
    }
}
