using Spectralis.Core.Common;

namespace Spectralis.Core.Metadata;

/// <summary>Watches library folders for live add/remove/rename of audio files.</summary>
public sealed class LibraryWatcher : IDisposable
{
    private readonly List<FileSystemWatcher> _watchers = [];

    public event EventHandler<string>? FileAdded;
    public event EventHandler<string>? FileRemoved;
    public event EventHandler<(string OldPath, string NewPath)>? FileRenamed;

    public void Watch(IReadOnlyList<string> folders)
    {
        StopAll();
        foreach (var folder in folders)
        {
            if (!Directory.Exists(folder))
            {
                continue;
            }

            try
            {
                var watcher = new FileSystemWatcher(folder)
                {
                    IncludeSubdirectories = true,
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName,
                    EnableRaisingEvents = true,
                };
                watcher.Created += (_, e) =>
                {
                    if (IsAudio(e.FullPath))
                    {
                        FileAdded?.Invoke(this, e.FullPath);
                    }
                };
                watcher.Deleted += (_, e) =>
                {
                    if (IsAudio(e.FullPath))
                    {
                        FileRemoved?.Invoke(this, e.FullPath);
                    }
                };
                watcher.Renamed += (_, e) =>
                {
                    if (IsAudio(e.FullPath))
                    {
                        FileRenamed?.Invoke(this, (e.OldFullPath, e.FullPath));
                    }
                };
                _watchers.Add(watcher);
            }
            catch
            {
                // Folder vanished or is inaccessible; skip it.
            }
        }
    }

    private void StopAll()
    {
        foreach (var watcher in _watchers)
        {
            watcher.Dispose();
        }

        _watchers.Clear();
    }

    private static bool IsAudio(string path) => SupportedAudioFormats.IsSupportedExtension(path);

    public void Dispose() => StopAll();
}
