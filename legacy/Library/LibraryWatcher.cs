using System.IO;

namespace Spectralis;

internal sealed class LibraryWatcher : IDisposable
{
    private static readonly string[] AudioExtensions =
    [
        ".mp3", ".flac", ".wav", ".ogg", ".m4a", ".aac",
        ".opus", ".wma", ".mid", ".midi"
    ];

    private readonly List<FileSystemWatcher> _watchers = [];

    public event EventHandler<string>? FileAdded;
    public event EventHandler<string>? FileRemoved;
    public event EventHandler<(string OldPath, string NewPath)>? FileRenamed;

    public void Watch(IReadOnlyList<string> folders)
    {
        StopAll();
        foreach (var folder in folders)
        {
            if (!Directory.Exists(folder)) continue;
            try
            {
                var w = new FileSystemWatcher(folder)
                {
                    IncludeSubdirectories = true,
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName,
                    EnableRaisingEvents = true
                };
                w.Created += (_, e) => { if (IsAudio(e.FullPath)) FileAdded?.Invoke(this, e.FullPath); };
                w.Deleted += (_, e) => { if (IsAudio(e.FullPath)) FileRemoved?.Invoke(this, e.FullPath); };
                w.Renamed += (_, e) => { if (IsAudio(e.FullPath)) FileRenamed?.Invoke(this, (e.OldFullPath, e.FullPath)); };
                _watchers.Add(w);
            }
            catch { }
        }
    }

    private void StopAll()
    {
        foreach (var w in _watchers) w.Dispose();
        _watchers.Clear();
    }

    private static bool IsAudio(string path) =>
        AudioExtensions.Contains(
            System.IO.Path.GetExtension(path).ToLowerInvariant());

    public void Dispose() => StopAll();
}
