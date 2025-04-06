using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;

namespace Spectralis.Library
{
    public class LibraryManager : IDisposable
    {
        private readonly LibraryDb _db;
        private readonly LibraryRepository _repo;
        private readonly LibraryScanner _scanner;
        private readonly FolderWatcher _watcher;
        private readonly string _configPath;

        private List<string> _watchedFolders = new List<string>();

        public LibraryRepository Repository => _repo;
        public LibraryScanner Scanner => _scanner;

        public event EventHandler LibraryChanged;

        public LibraryManager(string appDataPath)
        {
            string dbPath = Path.Combine(appDataPath, "library.db");
            _configPath = Path.Combine(appDataPath, "library-config.json");

            _db = new LibraryDb(dbPath);
            _repo = new LibraryRepository(_db);
            _scanner = new LibraryScanner(_repo);
            _watcher = new FolderWatcher(_repo);

            _watcher.TrackAdded += (s, e) => LibraryChanged?.Invoke(this, EventArgs.Empty);
            _watcher.TrackRemoved += (s, e) => LibraryChanged?.Invoke(this, EventArgs.Empty);
            _scanner.ScanComplete += (s, e) => LibraryChanged?.Invoke(this, EventArgs.Empty);

            LoadConfig();
        }

        public IReadOnlyList<string> WatchedFolders => _watchedFolders;

        public void AddFolder(string path)
        {
            if (_watchedFolders.Contains(path)) return;
            _watchedFolders.Add(path);
            _watcher.Watch(path);
            _scanner.ScanAsync(new[] { path });
            SaveConfig();
        }

        public void RemoveFolder(string path)
        {
            _watchedFolders.Remove(path);
            _watcher.StopAll();
            foreach (var f in _watchedFolders)
                _watcher.Watch(f);
            SaveConfig();
        }

        public void RescanAll()
        {
            _scanner.ScanAsync(_watchedFolders);
        }

        private void LoadConfig()
        {
            if (!File.Exists(_configPath)) return;
            try
            {
                var cfg = JsonConvert.DeserializeObject<LibraryConfig>(File.ReadAllText(_configPath));
                _watchedFolders = cfg?.WatchedFolders ?? new List<string>();
                foreach (var f in _watchedFolders)
                    _watcher.Watch(f);
            }
            catch { }
        }

        private void SaveConfig()
        {
            try
            {
                var cfg = new LibraryConfig { WatchedFolders = _watchedFolders };
                File.WriteAllText(_configPath, JsonConvert.SerializeObject(cfg, Formatting.Indented));
            }
            catch { }
        }

        public void Dispose()
        {
            _watcher?.Dispose();
            _db?.Dispose();
        }

        private class LibraryConfig
        {
            public List<string> WatchedFolders { get; set; } = new List<string>();
        }
    }
}
