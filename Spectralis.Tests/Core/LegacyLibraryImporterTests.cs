using Microsoft.Data.Sqlite;
using Spectralis.Core.Metadata;
using Xunit;

namespace Spectralis.Tests.Core;

public sealed class LegacyLibraryImporterTests : IDisposable
{
    private readonly string _dir;
    private readonly string _legacyDbPath;
    private readonly string _newDbPath;
    private readonly string _logPath;

    public LegacyLibraryImporterTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), $"spectralis-legacy-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_dir);
        _legacyDbPath = Path.Combine(_dir, "library.db");
        _newDbPath = Path.Combine(_dir, "library-avalonia.db");
        _logPath = Path.Combine(_dir, "import-log.txt");
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }

    /// <summary>Creates a database with the exact WinForms LibraryStore schema.</summary>
    private void CreateLegacyDb(params (string Path, string Title)[] tracks)
    {
        // Pooling=false: the fixture writer must fully release the file handle at
        // dispose so byte-level comparisons of library.db can open it afterwards.
        using var connection = new SqliteConnection($"Data Source={_legacyDbPath};Pooling=false");
        connection.Open();
        using (var create = connection.CreateCommand())
        {
            create.CommandText =
                """
                CREATE TABLE tracks (
                    path         TEXT PRIMARY KEY,
                    title        TEXT NOT NULL DEFAULT '',
                    artist       TEXT NOT NULL DEFAULT '',
                    album        TEXT NOT NULL DEFAULT '',
                    album_artist TEXT NOT NULL DEFAULT '',
                    genre        TEXT NOT NULL DEFAULT '',
                    year         INTEGER NOT NULL DEFAULT 0,
                    duration_sec REAL NOT NULL DEFAULT 0,
                    play_count   INTEGER NOT NULL DEFAULT 0,
                    date_added   TEXT NOT NULL DEFAULT '',
                    last_played  TEXT,
                    bpm          REAL,
                    key_name     TEXT
                );
                """;
            create.ExecuteNonQuery();
        }

        foreach (var (path, title) in tracks)
        {
            using var insert = connection.CreateCommand();
            insert.CommandText = "INSERT INTO tracks (path, title, artist) VALUES ($p, $t, 'Legacy Override Artist')";
            insert.Parameters.AddWithValue("$p", path);
            insert.Parameters.AddWithValue("$t", title);
            insert.ExecuteNonQuery();
        }
    }

    private string MakeWav(string name)
    {
        var source = WavFixture.CreateSineWav(0.1);
        var dest = Path.Combine(_dir, name);
        File.Move(source, dest);
        return dest;
    }

    [Fact]
    public void LegacyDatabaseExists_ChecksConfiguredPath()
    {
        Assert.False(LegacyLibraryImporter.LegacyDatabaseExists(_legacyDbPath));
        CreateLegacyDb();
        Assert.True(LegacyLibraryImporter.LegacyDatabaseExists(_legacyDbPath));
    }

    [Fact]
    public async Task Import_RescansExistingFilesAndSkipsMissing()
    {
        var alive1 = MakeWav("alive1.wav");
        var alive2 = MakeWav("alive2.wav");
        var gone = Path.Combine(_dir, "deleted.mp3");
        CreateLegacyDb((alive1, "Legacy Title 1"), (alive2, "Legacy Title 2"), (gone, "Gone"));

        using var target = new LibraryDatabase(_newDbPath);
        var result = await LegacyLibraryImporter.ImportAsync(_legacyDbPath, target, logPath: _logPath);

        Assert.Equal(2, result.Imported);
        Assert.Equal(1, result.Skipped);
        Assert.Equal(gone, Assert.Single(result.MissingPaths));
        Assert.Equal(2, target.Count());

        // Rescan policy: tags come from disk, not from the legacy rows. The
        // generated WAVs carry no tags, so the file-name fallback applies and
        // the legacy override artist must NOT survive.
        var imported = target.GetAllTracks();
        Assert.All(imported, track => Assert.NotEqual("Legacy Override Artist", track.Artist));
        Assert.Contains(imported, track => track.DisplayTitle == "alive1");
    }

    [Fact]
    public async Task Import_WritesMigrationLogWithMissingPaths()
    {
        var alive = MakeWav("alive.wav");
        var gone = Path.Combine(_dir, "gone.flac");
        CreateLegacyDb((alive, "A"), (gone, "B"));

        using var target = new LibraryDatabase(_newDbPath);
        var result = await LegacyLibraryImporter.ImportAsync(_legacyDbPath, target, logPath: _logPath);

        Assert.Equal(_logPath, result.LogPath);
        var log = File.ReadAllText(_logPath);
        Assert.Contains("Skipped (no longer on disk): 1", log);
        Assert.Contains(gone, log);
        Assert.Contains("source left untouched", log);
    }

    [Fact]
    public async Task Import_LeavesLegacyDatabaseUntouched()
    {
        var alive = MakeWav("alive.wav");
        CreateLegacyDb((alive, "A"));
        var before = File.ReadAllBytes(_legacyDbPath);

        using var target = new LibraryDatabase(_newDbPath);
        await LegacyLibraryImporter.ImportAsync(_legacyDbPath, target, logPath: _logPath);
        SqliteConnection.ClearAllPools();

        Assert.Equal(before, File.ReadAllBytes(_legacyDbPath));
    }

    [Fact]
    public async Task Import_EmptyLegacyDb_SucceedsWithZeroCounts()
    {
        CreateLegacyDb();
        using var target = new LibraryDatabase(_newDbPath);

        var result = await LegacyLibraryImporter.ImportAsync(_legacyDbPath, target, logPath: _logPath);

        Assert.Equal(0, result.Imported);
        Assert.Equal(0, result.Skipped);
    }
}
