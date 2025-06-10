using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Spectralis.Streaming
{
    public class StreamingTrackCache
    {
        private readonly string _cacheDir;
        private readonly Dictionary<string, string> _pathMap = new Dictionary<string, string>();
        private readonly long _maxSizeBytes;
        private long _currentSizeBytes;

        public StreamingTrackCache(string cacheDir, long maxSizeMb = 512)
        {
            _cacheDir = cacheDir;
            _maxSizeBytes = maxSizeMb * 1024 * 1024;
            Directory.CreateDirectory(cacheDir);
        }

        public bool TryGetCached(string trackId, out string path)
        {
            if (_pathMap.TryGetValue(trackId, out path) && File.Exists(path))
                return true;
            path = null;
            return false;
        }

        public async Task<string> StoreAsync(string trackId, Stream source, string ext = ".mp3", CancellationToken ct = default)
        {
            if (TryGetCached(trackId, out var existing))
                return existing;

            EvictIfNeeded();

            var path = Path.Combine(_cacheDir, $"{trackId}{ext}");
            using var file = File.Create(path);
            await source.CopyToAsync(file, 81920, ct);

            long size = new FileInfo(path).Length;
            _currentSizeBytes += size;
            _pathMap[trackId] = path;
            return path;
        }

        private void EvictIfNeeded()
        {
            if (_currentSizeBytes < _maxSizeBytes) return;

            foreach (var f in new DirectoryInfo(_cacheDir).GetFiles())
            {
                long sz = f.Length;
                f.Delete();
                _currentSizeBytes = Math.Max(0, _currentSizeBytes - sz);
                if (_currentSizeBytes < _maxSizeBytes * 0.75) break;
            }

            _pathMap.Clear();
        }
    }
}
