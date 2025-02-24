using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;

namespace Spectralis.Audio
{
    public class RecentFiles
    {
        private const int MaxItems = 10;
        private readonly string _storePath;
        private readonly List<string> _items;

        public IReadOnlyList<string> Items => _items;

        public RecentFiles(string storePath)
        {
            _storePath = storePath;
            _items = Load();
        }

        public void Add(string filePath)
        {
            _items.Remove(filePath);
            _items.Insert(0, filePath);
            if (_items.Count > MaxItems)
                _items.RemoveAt(_items.Count - 1);
            Save();
        }

        public void Clear()
        {
            _items.Clear();
            Save();
        }

        private List<string> Load()
        {
            if (!File.Exists(_storePath)) return new List<string>();
            try { return JsonConvert.DeserializeObject<List<string>>(File.ReadAllText(_storePath)) ?? new List<string>(); }
            catch { return new List<string>(); }
        }

        private void Save()
        {
            try { File.WriteAllText(_storePath, JsonConvert.SerializeObject(_items, Formatting.Indented)); }
            catch { }
        }
    }
}
