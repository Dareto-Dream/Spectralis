using System.Globalization;
using System.IO;
using Microsoft.Data.Sqlite;

namespace Spectralis;

internal sealed class LibraryStore : IDisposable
{
    private static string DbPath => System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Spectralis", "library.db");

    private readonly SqliteConnection _db;

    public LibraryStore()
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(DbPath)!);
        _db = new SqliteConnection($"Data Source={DbPath}");
        _db.Open();
        InitSchema();
    }

    private void InitSchema()
    {
        using var cmd = _db.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS tracks (
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
                last_played  TEXT
            );
            """;
        cmd.ExecuteNonQuery();
        MigrateAnalysisColumns();
    }

    private void MigrateAnalysisColumns()
    {
        // Add bpm and key_name columns if they don't exist yet (SQLite migration)
        foreach (var ddl in new[] {
            "ALTER TABLE tracks ADD COLUMN bpm REAL",
            "ALTER TABLE tracks ADD COLUMN key_name TEXT"
        })
        {
            try
            {
                using var c = _db.CreateCommand();
                c.CommandText = ddl;
                c.ExecuteNonQuery();
            }
            catch { /* column already exists */ }
        }
    }

    public List<LibraryTrack> GetAll()
    {
        var result = new List<LibraryTrack>();
        using var cmd = _db.CreateCommand();
        cmd.CommandText =
            "SELECT path,title,artist,album,album_artist,genre,year,duration_sec," +
            "play_count,date_added,last_played,bpm,key_name FROM tracks ORDER BY artist,album,title";
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            result.Add(ReadRow(reader));
        return result;
    }

    public void Upsert(LibraryTrack t)
    {
        try
        {
            using var cmd = _db.CreateCommand();
            cmd.CommandText = """
                INSERT INTO tracks
                    (path,title,artist,album,album_artist,genre,year,duration_sec,play_count,date_added,last_played,bpm,key_name)
                VALUES
                    ($p,$ti,$ar,$al,$aa,$ge,$yr,$dur,$plays,$added,$last,$bpm,$key)
                ON CONFLICT(path) DO UPDATE SET
                    title=$ti, artist=$ar, album=$al, album_artist=$aa,
                    genre=$ge, year=$yr, duration_sec=$dur,
                    play_count=$plays, date_added=$added, last_played=$last,
                    bpm=$bpm, key_name=$key
                """;
            cmd.Parameters.AddWithValue("$p",     t.Path);
            cmd.Parameters.AddWithValue("$ti",    t.Title);
            cmd.Parameters.AddWithValue("$ar",    t.Artist);
            cmd.Parameters.AddWithValue("$al",    t.Album);
            cmd.Parameters.AddWithValue("$aa",    t.AlbumArtist);
            cmd.Parameters.AddWithValue("$ge",    t.Genre);
            cmd.Parameters.AddWithValue("$yr",    t.Year);
            cmd.Parameters.AddWithValue("$dur",   t.DurationSeconds);
            cmd.Parameters.AddWithValue("$plays", t.PlayCount);
            cmd.Parameters.AddWithValue("$added", t.DateAdded.ToString("O"));
            cmd.Parameters.AddWithValue("$last",  t.LastPlayed.HasValue
                ? (object)t.LastPlayed.Value.ToString("O")
                : DBNull.Value);
            cmd.Parameters.AddWithValue("$bpm",   t.Bpm.HasValue ? (object)t.Bpm.Value : DBNull.Value);
            cmd.Parameters.AddWithValue("$key",   string.IsNullOrWhiteSpace(t.Key) ? DBNull.Value : (object)t.Key);
            cmd.ExecuteNonQuery();
        }
        catch { }
    }

    public void UpdateAnalysis(string path, float? bpm, string? key)
    {
        try
        {
            using var cmd = _db.CreateCommand();
            cmd.CommandText = "UPDATE tracks SET bpm=$bpm, key_name=$key WHERE path=$p";
            cmd.Parameters.AddWithValue("$p",   path);
            cmd.Parameters.AddWithValue("$bpm", bpm.HasValue ? (object)bpm.Value : DBNull.Value);
            cmd.Parameters.AddWithValue("$key", string.IsNullOrWhiteSpace(key) ? DBNull.Value : (object)key);
            cmd.ExecuteNonQuery();
        }
        catch { }
    }

    public void Delete(string path)
    {
        try
        {
            using var cmd = _db.CreateCommand();
            cmd.CommandText = "DELETE FROM tracks WHERE path=$p";
            cmd.Parameters.AddWithValue("$p", path);
            cmd.ExecuteNonQuery();
        }
        catch { }
    }

    private static LibraryTrack ReadRow(SqliteDataReader r) => new(
        Path:            r.GetString(0),
        Title:           r.IsDBNull(1)  ? "" : r.GetString(1),
        Artist:          r.IsDBNull(2)  ? "" : r.GetString(2),
        Album:           r.IsDBNull(3)  ? "" : r.GetString(3),
        AlbumArtist:     r.IsDBNull(4)  ? "" : r.GetString(4),
        Genre:           r.IsDBNull(5)  ? "" : r.GetString(5),
        Year:            r.IsDBNull(6)  ? 0  : r.GetInt32(6),
        DurationSeconds: r.IsDBNull(7)  ? 0  : r.GetDouble(7),
        PlayCount:       r.IsDBNull(8)  ? 0  : r.GetInt32(8),
        DateAdded:       r.IsDBNull(9)  ? DateTime.UtcNow
                         : DateTime.Parse(r.GetString(9), null, DateTimeStyles.RoundtripKind),
        LastPlayed:      r.IsDBNull(10) ? null
                         : DateTime.Parse(r.GetString(10), null, DateTimeStyles.RoundtripKind),
        Bpm:             r.FieldCount > 11 && !r.IsDBNull(11) ? (float?)r.GetDouble(11) : null,
        Key:             r.FieldCount > 12 && !r.IsDBNull(12) ? r.GetString(12) : null
    );

    public void Dispose() => _db.Dispose();
}
