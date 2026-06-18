using System;
using System.IO;
using System.Timers;

namespace Spectralis.Core.Library
{
    public class FolderWatcher : IDisposable
    {
        private readonly FileSystemWatcher _watcher;
        private readonly Timer _debounce;
        private string? _pendingPath;
        private bool _disposed;

        public event EventHandler<string>? FileAdded;
        public event EventHandler<string>? FileRemoved;
        public event EventHandler<string>? FileChanged;

        public FolderWatcher(string folder, int debounceMs = 500)
        {
            _watcher = new FileSystemWatcher(folder)
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
                Filter = "*.*",
                EnableRaisingEvents = false
            };

            _debounce = new Timer(debounceMs) { AutoReset = false };
            _debounce.Elapsed += OnDebounceElapsed;

            _watcher.Created += (s, e) => Debounce(e.FullPath, FileAdded);
            _watcher.Deleted += (s, e) => FileRemoved?.Invoke(this, e.FullPath);
            _watcher.Changed += (s, e) => Debounce(e.FullPath, FileChanged);
            _watcher.Renamed += (s, e) =>
            {
                FileRemoved?.Invoke(this, e.OldFullPath);
                Debounce(e.FullPath, FileAdded);
            };
        }

        private EventHandler<string>? _pendingHandler;

        private void Debounce(string path, EventHandler<string>? handler)
        {
            _pendingPath = path;
            _pendingHandler = handler;
            _debounce.Stop();
            _debounce.Start();
        }

        private void OnDebounceElapsed(object? sender, ElapsedEventArgs e)
        {
            if (_pendingPath != null)
                _pendingHandler?.Invoke(this, _pendingPath);
        }

        public void Start() => _watcher.EnableRaisingEvents = true;
        public void Stop() => _watcher.EnableRaisingEvents = false;

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _watcher.Dispose();
            _debounce.Dispose();
        }
    }
}
