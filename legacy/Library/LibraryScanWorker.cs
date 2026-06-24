using System.IO;

namespace Spectralis;

internal sealed class LibraryScanWorker
{
    private static readonly string[] AudioExtensions =
    [
        ".mp3", ".flac", ".wav", ".ogg", ".m4a", ".aac",
        ".opus", ".wma", ".mid", ".midi"
    ];

    public async Task ScanAsync(
        IReadOnlyList<string> folders,
        MusicLibrary library,
        IProgress<int>? progress = null,
        CancellationToken ct = default)
    {
        var files = await Task.Run(() => CollectFiles(folders), ct);

        var total = files.Count;
        var done = 0;

        foreach (var path in files)
        {
            ct.ThrowIfCancellationRequested();
            var track = await Task.Run(() => ScanFile(path, library), ct);
            if (track is not null)
                library.Upsert(track);

            progress?.Report(++done * 100 / Math.Max(1, total));
        }

        // Remove DB entries for files that no longer exist under watched folders
        var pathSet = new HashSet<string>(files, StringComparer.OrdinalIgnoreCase);
        foreach (var track in library.Tracks.ToList())
        {
            if (!pathSet.Contains(track.Path) &&
                folders.Any(f => track.Path.StartsWith(f, StringComparison.OrdinalIgnoreCase)))
            {
                library.Remove(track.Path);
            }
        }
    }

    private static List<string> CollectFiles(IReadOnlyList<string> folders)
    {
        var result = new List<string>();
        foreach (var folder in folders)
        {
            if (!Directory.Exists(folder)) continue;
            try
            {
                result.AddRange(
                    Directory.EnumerateFiles(folder, "*.*", SearchOption.AllDirectories)
                        .Where(f => AudioExtensions.Contains(
                            System.IO.Path.GetExtension(f).ToLowerInvariant())));
            }
            catch { }
        }
        return result;
    }

    private static LibraryTrack? ScanFile(string path, MusicLibrary library)
    {
        try
        {
            var existing = library.Find(path);
            using var file = TagLib.File.Create(path);
            var tag = file.Tag;
            return new LibraryTrack(
                Path:            path,
                Title:           string.IsNullOrWhiteSpace(tag.Title)
                                 ? System.IO.Path.GetFileNameWithoutExtension(path)
                                 : tag.Title.Trim(),
                Artist:          tag.FirstPerformer?.Trim() ?? "",
                Album:           tag.Album?.Trim() ?? "",
                AlbumArtist:     tag.FirstAlbumArtist?.Trim() ?? "",
                Genre:           tag.FirstGenre?.Trim() ?? "",
                Year:            (int)tag.Year,
                DurationSeconds: file.Properties.Duration.TotalSeconds,
                PlayCount:       existing?.PlayCount ?? 0,
                DateAdded:       existing?.DateAdded ?? DateTime.UtcNow,
                LastPlayed:      existing?.LastPlayed
            );
        }
        catch
        {
            return null;
        }
    }
}
