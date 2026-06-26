using Spectralis.Core.Common;

namespace Spectralis.Core.Metadata;

public sealed record LibraryScanProgress(int Scanned, int Total, string CurrentFile, bool Completed);

public sealed record LibraryScanResult(int Added, int Updated, int Unchanged, int MarkedMissing, int Failed);

/// <summary>
/// Incremental, cancellable library scan. Unchanged files (same mtime+size) are
/// skipped without opening them; malformed files index with file-name fallback
/// metadata and never abort the scan.
/// </summary>
public sealed class LibraryScanner
{
    private readonly LibraryDatabase _database;

    public LibraryScanner(LibraryDatabase database) => _database = database;

    public async Task<LibraryScanResult> ScanAsync(
        IReadOnlyList<string> rootPaths,
        IProgress<LibraryScanProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        return await Task.Run(() => Scan(rootPaths, progress, cancellationToken), cancellationToken);
    }

    private LibraryScanResult Scan(
        IReadOnlyList<string> rootPaths,
        IProgress<LibraryScanProgress>? progress,
        CancellationToken ct)
    {
        var files = new List<string>();
        foreach (var root in rootPaths)
        {
            ct.ThrowIfCancellationRequested();
            CollectFiles(root, files, ct);
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int added = 0, updated = 0, unchanged = 0, failed = 0;

        for (var index = 0; index < files.Count; index++)
        {
            ct.ThrowIfCancellationRequested();
            var path = files[index];
            seen.Add(path);

            try
            {
                var fileInfo = new FileInfo(path);
                var mtime = fileInfo.LastWriteTimeUtc.Ticks;
                var fingerprint = _database.GetFingerprint(path);

                if (fingerprint is { } existing && existing.MtimeTicks == mtime && existing.FileSize == fileInfo.Length)
                {
                    unchanged++;
                }
                else
                {
                    var track = TrackMetadataReader.Read(path);
                    if (string.IsNullOrWhiteSpace(track.FormatName))
                    {
                        track = track with { FormatName = FormatLabel.FromExtension(path) };
                    }

                    _database.Upsert(track, mtime);
                    if (fingerprint is null)
                        added++;
                    else
                        updated++;
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                failed++;
            }

            if (progress is not null && (index % 25 == 0 || index == files.Count - 1))
            {
                progress.Report(new LibraryScanProgress(index + 1, files.Count, path, Completed: false));
            }
        }

        // Files indexed under these roots that no longer exist get flagged, not deleted —
        // the missing-file recovery flow restores them if the path reappears.
        var markedMissing = 0;
        foreach (var knownPath in _database.GetAllPaths())
        {
            ct.ThrowIfCancellationRequested();
            var underRoot = rootPaths.Any(root =>
                knownPath.StartsWith(root, StringComparison.OrdinalIgnoreCase));
            if (underRoot && !seen.Contains(knownPath) && !File.Exists(knownPath))
            {
                _database.MarkMissing(knownPath);
                markedMissing++;
            }
        }

        progress?.Report(new LibraryScanProgress(files.Count, files.Count, string.Empty, Completed: true));
        return new LibraryScanResult(added, updated, unchanged, markedMissing, failed);
    }

    private static void CollectFiles(string root, List<string> files, CancellationToken ct)
    {
        if (File.Exists(root))
        {
            if (SupportedAudioFormats.IsSupportedExtension(root))
            {
                files.Add(root);
            }

            return;
        }

        if (!Directory.Exists(root))
        {
            return;
        }

        var pending = new Stack<string>();
        pending.Push(root);

        while (pending.Count > 0)
        {
            ct.ThrowIfCancellationRequested();
            var directory = pending.Pop();

            try
            {
                foreach (var subdirectory in Directory.EnumerateDirectories(directory))
                {
                    pending.Push(subdirectory);
                }

                foreach (var file in Directory.EnumerateFiles(directory))
                {
                    if (SupportedAudioFormats.IsSupportedExtension(file))
                    {
                        files.Add(file);
                    }
                }
            }
            catch (UnauthorizedAccessException)
            {
                // Inaccessible directories are skipped, not fatal.
            }
            catch (IOException)
            {
            }
        }
    }
}
