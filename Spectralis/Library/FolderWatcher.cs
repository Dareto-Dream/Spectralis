using System;
using System.Collections.Generic;
using System.IO;
using Spectralis.Audio;

namespace Spectralis.Library
{
    public class FolderWatcher : IDisposable
    {
        private readonly List<FileSystemWatcher> _watchers = new List<FileSystemWatcher>();
        private readonly LibraryRepository _repo;

        public event EventHandler<string> TrackAdded;
        public event EventHandler<string> TrackRemoved;
        public event EventHandler<(string OldPath, string NewPath)> TrackRenamed;

        public FolderWatcher(LibraryRepository repo)
        {
            _repo = repo;
        }

        public void Watch(string folderPath)
        {
            if (!Directory.Exists(folderPath)) return;

            var watcher = new FileSystemWatcher(folderPath)
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite,
                Filter = "*.*",
                EnableRaisingEvents = true
            };

            watcher.Created += OnCreated;
            watcher.Deleted += OnDeleted;
            watcher.Renamed += OnRenamed;

            _watchers.Add(watcher);
        }

        public void StopAll()
        {
            foreach (var w in _watchers)
            {
                w.EnableRaisingEvents = false;
                w.Dispose();
            }
            _watchers.Clear();
        }

        private void OnCreated(object sender, FileSystemEventArgs e)
        {
            if (!FormatDetector.IsSupported(e.FullPath)) return;
            try
            {
                var info = MetadataExtractor.Extract(e.FullPath);
                var track = new LibraryTrack
                {
                    Path = e.FullPath,
                    Title = info.Title,
                    Artist = info.Artist,
                    Album = info.Album,
                    DurationMs = (long)info.Duration.TotalMilliseconds,
                    Format = info.Format
                };
                _repo.Upsert(track);
                TrackAdded?.Invoke(this, e.FullPath);
            }
            catch { }
        }

        private void OnDeleted(object sender, FileSystemEventArgs e)
        {
            if (!FormatDetector.IsSupported(e.FullPath)) return;
            _repo.Delete(e.FullPath);
            TrackRemoved?.Invoke(this, e.FullPath);
        }

        private void OnRenamed(object sender, RenamedEventArgs e)
        {
            if (!FormatDetector.IsSupported(e.FullPath)) return;
            _repo.Delete(e.OldFullPath);
            OnCreated(sender, e);
            TrackRenamed?.Invoke(this, (e.OldFullPath, e.FullPath));
        }

        public void Dispose() => StopAll();
    }
}
