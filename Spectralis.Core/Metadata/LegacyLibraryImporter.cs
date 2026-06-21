using Microsoft.Data.Sqlite;

namespace Spectralis.Core.Metadata;

public sealed record LegacyImportResult(
    int Imported,
    int Skipped,
    IReadOnlyList<string> MissingPaths,
    string? LogPath);

/// <summary>
/// One-time import from the WinForms library.db. Rescan policy: legacy tag
/// overrides are discarded — every recorded path is re-read from disk through
/// the new scanner so the index reflects actual file state. Missing files are
/// skipped and written to a migration log, never added. The legacy database is
/// opened read-only and left untouched.
/// </summary>
public static class LegacyLibraryImporter
{
    public static string DefaultLegacyDatabasePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Spectralis", "library.db");

    public static string DefaultLogPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Spectralis", "legacy-import-log.txt");

    public static bool LegacyDatabaseExists(string? legacyDbPath = null) =>
        File.Exists(legacyDbPath ?? DefaultLegacyDatabasePath);

    /// <summary>Reads every track path recorded by the legacy WinForms app.</summary>
    public static IReadOnlyList<string> ReadLegacyPaths(string legacyDbPath)
    {
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = legacyDbPath,
            Mode = SqliteOpenMode.ReadOnly,
            // No pooling: the file handle must release at dispose so the legacy
            // database is never held open after the one-time read.
            Pooling = false,
        }.ToString();

        var paths = new List<string>();
        using var connection = new SqliteConnection(connectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT path FROM tracks";
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var path = reader.GetString(0);
            if (!string.IsNullOrWhiteSpace(path))
            {
                paths.Add(path);
            }
        }

        return paths;
    }

    public static async Task<LegacyImportResult> ImportAsync(
        string legacyDbPath,
        LibraryDatabase target,
        IProgress<LibraryScanProgress>? progress = null,
        string? logPath = null,
        CancellationToken cancellationToken = default)
    {
        var legacyPaths = ReadLegacyPaths(legacyDbPath);

        var existing = new List<string>();
        var missing = new List<string>();
        foreach (var path in legacyPaths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (File.Exists(path))
            {
                existing.Add(path);
            }
            else
            {
                missing.Add(path);
            }
        }

        // Fresh metadata read from disk for every surviving file — the scanner
        // treats each path as a single-file root.
        var scanner = new LibraryScanner(target);
        var scan = await scanner.ScanAsync(existing, progress, cancellationToken);

        var resolvedLogPath = WriteLog(logPath ?? DefaultLogPath, legacyDbPath, legacyPaths.Count, scan, missing);

        return new LegacyImportResult(
            Imported: scan.Added + scan.Updated + scan.Unchanged,
            Skipped: missing.Count,
            MissingPaths: missing,
            LogPath: resolvedLogPath);
    }

    private static string? WriteLog(
        string logPath,
        string legacyDbPath,
        int totalLegacyEntries,
        LibraryScanResult scan,
        IReadOnlyList<string> missing)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);
            using var writer = new StreamWriter(logPath, append: false);
            writer.WriteLine($"Legacy library import — {DateTimeOffset.Now:u}");
            writer.WriteLine($"Source: {legacyDbPath} ({totalLegacyEntries} entries; source left untouched)");
            writer.WriteLine($"Indexed from disk: {scan.Added + scan.Updated + scan.Unchanged} (added {scan.Added}, updated {scan.Updated}, already current {scan.Unchanged}, unreadable {scan.Failed})");
            writer.WriteLine($"Skipped (no longer on disk): {missing.Count}");
            foreach (var path in missing)
            {
                writer.WriteLine($"  missing: {path}");
            }

            return logPath;
        }
        catch
        {
            // The import itself succeeded; an unwritable log must not fail it.
            return null;
        }
    }
}
