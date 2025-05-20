using System;
using System.Collections.Generic;
using System.Linq;

namespace Spectralis.Streaming
{
    public class SoundCloudResultCache
    {
        private class CacheEntry
        {
            public List<StreamingTrack> Results { get; set; }
            public DateTime CachedAt { get; set; }
        }

        private readonly Dictionary<string, CacheEntry> _cache = new Dictionary<string, CacheEntry>(StringComparer.OrdinalIgnoreCase);
        private readonly TimeSpan _ttl;
        private readonly int _maxKeys;

        public SoundCloudResultCache(TimeSpan? ttl = null, int maxKeys = 100)
        {
            _ttl = ttl ?? TimeSpan.FromMinutes(10);
            _maxKeys = maxKeys;
        }

        public bool TryGet(string query, out List<StreamingTrack> results)
        {
            if (_cache.TryGetValue(query, out var entry) && DateTime.UtcNow - entry.CachedAt < _ttl)
            {
                results = entry.Results;
                return true;
            }
            results = null;
            return false;
        }

        public void Set(string query, List<StreamingTrack> results)
        {
            if (_cache.Count >= _maxKeys)
            {
                string oldest = _cache.OrderBy(kv => kv.Value.CachedAt).First().Key;
                _cache.Remove(oldest);
            }

            _cache[query] = new CacheEntry { Results = results, CachedAt = DateTime.UtcNow };
        }

        public void Invalidate(string query) => _cache.Remove(query);

        public void Clear() => _cache.Clear();
    }
}
