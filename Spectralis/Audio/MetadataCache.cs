using System;
using System.Collections.Generic;

namespace Spectralis.Audio
{
    public class MetadataCache
    {
        private readonly Dictionary<string, TrackInfo> _cache = new Dictionary<string, TrackInfo>(StringComparer.OrdinalIgnoreCase);
        private readonly int _maxEntries;

        public MetadataCache(int maxEntries = 500)
        {
            _maxEntries = maxEntries;
        }

        public TrackInfo Get(string filePath)
        {
            return _cache.TryGetValue(filePath, out var info) ? info : null;
        }

        public void Set(string filePath, TrackInfo info)
        {
            if (_cache.Count >= _maxEntries)
                _cache.Clear();
            _cache[filePath] = info;
        }

        public bool Contains(string filePath) => _cache.ContainsKey(filePath);

        public void Invalidate(string filePath) => _cache.Remove(filePath);

        public void Clear() => _cache.Clear();

        public int Count => _cache.Count;
    }
}
