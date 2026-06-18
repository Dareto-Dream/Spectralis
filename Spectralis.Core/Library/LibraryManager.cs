using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Spectralis.Core.Models;

namespace Spectralis.Core.Library
{
    public class LibraryManager : ILibrary, IDisposable
    {
        private readonly LibraryDb _db;
        private readonly LibraryRepository _repo;
        private readonly LibraryScanner _scanner;
        private readonly List<FolderWatcher> _watchers = new List<FolderWatcher>();
        private bool _disposed;

        public event EventHandler<TrackInfo>? TrackAdded;
        public event EventHandler<TrackInfo>? TrackRemoved;
        public event EventHandler? LibraryChanged;

        public LibraryManager(string dbPath)
        {
            _db = new LibraryDb(dbPath);
            _repo = new LibraryRepository(_db);
            _scanner = new LibraryScanner(_repo);
            _scanner.TrackScanned += (s, t) =>
            {
                TrackAdded?.Invoke(this, t);
                LibraryChanged?.Invoke(this, EventArgs.Empty);
            };
        }

        public Task<IReadOnlyList<TrackInfo>> SearchAsync(string query, CancellationToken ct = default) =>
            _repo.SearchAsync(query, ct);

        public Task<IReadOnlyList<TrackInfo>> GetAllAsync(CancellationToken ct = default) =>
            _repo.GetAllAsync(ct);

        public async Task<IReadOnlyList<string>> GetArtistsAsync(CancellationToken ct = default)
        {
            var all = await _repo.GetAllAsync(ct);
            var set = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var t in all)
                if (!string.IsNullOrEmpty(t.Artist)) set.Add(t.Artist!);
            return new List<string>(set);
        }

        public async Task<IReadOnlyList<string>> GetAlbumsAsync(string? artist = null, CancellationToken ct = default)
        {
            var all = await _repo.GetAllAsync(ct);
            var set = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var t in all)
            {
                if (artist != null && !string.Equals(t.Artist, artist, StringComparison.OrdinalIgnoreCase)) continue;
                if (!string.IsNullOrEmpty(t.Album)) set.Add(t.Album!);
            }
            return new List<string>(set);
        }

        public Task<int> CountAsync(CancellationToken ct = default) =>
            _repo.CountAsync(ct);

        public async Task AddFolderAsync(string folderPath, IProgress<string>? progress = null, CancellationToken ct = default)
        {
            await _scanner.ScanFolderAsync(folderPath, progress, ct);

            var watcher = new FolderWatcher(folderPath);
            watcher.FileAdded += async (s, path) =>
            {
                var ext = System.IO.Path.GetExtension(path).ToLower();
                if (ext is ".mp3" or ".flac" or ".wav" or ".ogg" or ".aac" or ".m4a" or ".opus")
                {
                    var info = await new Audio.MetadataExtractor().LoadMetadataAsync(path);
                    await _repo.UpsertAsync(info);
                    TrackAdded?.Invoke(this, info);
                    LibraryChanged?.Invoke(this, EventArgs.Empty);
                }
            };
            watcher.FileRemoved += async (s, path) =>
            {
                await _repo.DeleteAsync(path);
                LibraryChanged?.Invoke(this, EventArgs.Empty);
            };
            watcher.Start();
            _watchers.Add(watcher);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            foreach (var w in _watchers) w.Dispose();
            _db.Dispose();
        }
    }
}
