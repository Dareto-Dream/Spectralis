using Microsoft.Data.Sqlite;
using Spectralis.Core.Common;

namespace Spectralis.Core.Metadata;

/// <summary>A library row: track metadata plus play stats.</summary>
public sealed record LibraryEntry(
    TrackInfo Track,
    int PlayCount,
    DateTime DateAdded,
    DateTime? LastPlayed);

/// <summary>Podcast-mode state for one track: whether it's a podcast and where playback left off.</summary>
public sealed record PodcastPlaybackState(bool IsPodcast, long ResumePositionMs, bool Finished)
{
    public static readonly PodcastPlaybackState None = new(false, 0, false);
}

/// <summary>A podcast/audiobook episode row for the Podcasts browser.</summary>
public sealed record PodcastEpisodeEntry(
    TrackInfo Track,
    long ResumePositionMs,
    bool Finished,
    DateTime? LastPlayed);

/// <summary>
/// SQLite-backed track index. One row per file; cover art is read on demand
/// from the file, never stored. Safe for concurrent readers, single writer.
/// </summary>
public sealed class LibraryDatabase : IDisposable
{
    private readonly string _connectionString;
    private readonly SqliteConnection _connection;
    private readonly object _writeLock = new();

    public LibraryDatabase(string databasePath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(databasePath)!);
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Cache = SqliteCacheMode.Shared,
        }.ToString();

        _connection = new SqliteConnection(_connectionString);
        _connection.Open();
        Execute(
            """
            CREATE TABLE IF NOT EXISTS tracks (
                path TEXT PRIMARY KEY,
                title TEXT NOT NULL DEFAULT '',
                artist TEXT NOT NULL DEFAULT '',
                album TEXT NOT NULL DEFAULT '',
                album_artist TEXT NOT NULL DEFAULT '',
                genre TEXT NOT NULL DEFAULT '',
                track_no INTEGER NOT NULL DEFAULT 0,
                disc_no INTEGER NOT NULL DEFAULT 0,
                year INTEGER NOT NULL DEFAULT 0,
                duration_ms INTEGER NOT NULL DEFAULT 0,
                bitrate_kbps INTEGER NOT NULL DEFAULT 0,
                sample_rate_hz INTEGER NOT NULL DEFAULT 0,
                channels INTEGER NOT NULL DEFAULT 0,
                file_size INTEGER NOT NULL DEFAULT 0,
                format TEXT NOT NULL DEFAULT '',
                bpm REAL NULL,
                musical_key TEXT NULL,
                mtime_ticks INTEGER NOT NULL DEFAULT 0,
                missing INTEGER NOT NULL DEFAULT 0
            );
            CREATE INDEX IF NOT EXISTS idx_tracks_title ON tracks(title);
            CREATE INDEX IF NOT EXISTS idx_tracks_artist ON tracks(artist);
            CREATE INDEX IF NOT EXISTS idx_tracks_album ON tracks(album);
            """);

        // Play-stats columns arrived after the first release; older databases migrate in place.
        TryAddColumn("play_count INTEGER NOT NULL DEFAULT 0");
        TryAddColumn("date_added INTEGER NOT NULL DEFAULT 0");
        TryAddColumn("last_played INTEGER NULL");

        // Podcast Mode: auto-detected podcast flag, a nullable manual override that wins over it,
        // and resume-from-position state (podcast content only).
        TryAddColumn("is_podcast INTEGER NOT NULL DEFAULT 0");
        TryAddColumn("podcast_override INTEGER NULL");
        TryAddColumn("resume_position_ms INTEGER NOT NULL DEFAULT 0");
        TryAddColumn("resume_updated_ticks INTEGER NOT NULL DEFAULT 0");
        TryAddColumn("is_finished INTEGER NOT NULL DEFAULT 0");
    }

    private void TryAddColumn(string columnDefinition)
    {
        try
        {
            Execute($"ALTER TABLE tracks ADD COLUMN {columnDefinition}");
        }
        catch (SqliteException)
        {
            // Column already exists.
        }
    }

    public void Upsert(TrackInfo track, long mtimeTicks) => Upsert(track, mtimeTicks, isPodcast: false);

    /// <summary>
    /// Inserts or refreshes a track row. <paramref name="isPodcast"/> is the auto-detected
    /// podcast flag; the nullable manual override (<see cref="SetPodcastOverride"/>) is never
    /// touched here, so a re-scan can't undo the user's choice.
    /// </summary>
    public void Upsert(TrackInfo track, long mtimeTicks, bool isPodcast)
    {
        lock (_writeLock)
        {
            using var command = _connection.CreateCommand();
            command.CommandText =
                """
                INSERT INTO tracks (
                    path, title, artist, album, album_artist, genre, track_no, disc_no, year,
                    duration_ms, bitrate_kbps, sample_rate_hz, channels, file_size, format,
                    bpm, musical_key, mtime_ticks, missing, date_added, is_podcast)
                VALUES (
                    $path, $title, $artist, $album, $albumArtist, $genre, $trackNo, $discNo, $year,
                    $durationMs, $bitrate, $sampleRate, $channels, $fileSize, $format,
                    $bpm, $key, $mtime, 0, $dateAdded, $isPodcast)
                ON CONFLICT(path) DO UPDATE SET
                    title = excluded.title, artist = excluded.artist, album = excluded.album,
                    album_artist = excluded.album_artist, genre = excluded.genre,
                    track_no = excluded.track_no, disc_no = excluded.disc_no, year = excluded.year,
                    duration_ms = excluded.duration_ms, bitrate_kbps = excluded.bitrate_kbps,
                    sample_rate_hz = excluded.sample_rate_hz, channels = excluded.channels,
                    file_size = excluded.file_size, format = excluded.format,
                    bpm = excluded.bpm, musical_key = excluded.musical_key,
                    mtime_ticks = excluded.mtime_ticks, missing = 0,
                    is_podcast = excluded.is_podcast
                """;
            command.Parameters.AddWithValue("$isPodcast", isPodcast ? 1 : 0);
            command.Parameters.AddWithValue("$path", track.SourcePath);
            command.Parameters.AddWithValue("$title", track.Title);
            command.Parameters.AddWithValue("$artist", track.Artist);
            command.Parameters.AddWithValue("$album", track.Album);
            command.Parameters.AddWithValue("$albumArtist", track.AlbumArtist);
            command.Parameters.AddWithValue("$genre", track.Genre);
            command.Parameters.AddWithValue("$trackNo", track.TrackNumber);
            command.Parameters.AddWithValue("$discNo", track.DiscNumber);
            command.Parameters.AddWithValue("$year", track.Year);
            command.Parameters.AddWithValue("$durationMs", (long)track.Duration.TotalMilliseconds);
            command.Parameters.AddWithValue("$bitrate", track.BitrateKbps);
            command.Parameters.AddWithValue("$sampleRate", track.SampleRateHz);
            command.Parameters.AddWithValue("$channels", track.Channels);
            command.Parameters.AddWithValue("$fileSize", track.FileSizeBytes);
            command.Parameters.AddWithValue("$format", track.FormatName);
            command.Parameters.AddWithValue("$bpm", (object?)track.Bpm ?? DBNull.Value);
            command.Parameters.AddWithValue("$key", (object?)track.MusicalKey ?? DBNull.Value);
            command.Parameters.AddWithValue("$mtime", mtimeTicks);
            command.Parameters.AddWithValue("$dateAdded", DateTime.UtcNow.Ticks);
            command.ExecuteNonQuery();
        }
    }

    /// <summary>Persists BPM/key analysis results for a file.</summary>
    public void UpdateAnalysis(string path, double bpm, string? musicalKey)
    {
        lock (_writeLock)
        {
            using var command = _connection.CreateCommand();
            command.CommandText =
                "UPDATE tracks SET bpm = $bpm, musical_key = $key WHERE path = $path";
            command.Parameters.AddWithValue("$path", path);
            command.Parameters.AddWithValue("$bpm", bpm);
            command.Parameters.AddWithValue("$key", (object?)musicalKey ?? DBNull.Value);
            command.ExecuteNonQuery();
        }
    }

    /// <summary>Bumps the play counter and stamps last-played; called on local track load.</summary>
    public void IncrementPlayCount(string path)
    {
        lock (_writeLock)
        {
            using var command = _connection.CreateCommand();
            command.CommandText =
                "UPDATE tracks SET play_count = play_count + 1, last_played = $now WHERE path = $path";
            command.Parameters.AddWithValue("$path", path);
            command.Parameters.AddWithValue("$now", DateTime.UtcNow.Ticks);
            command.ExecuteNonQuery();
        }
    }

    // ── Podcast Mode ────────────────────────────────────────────────────────

    /// <summary>Podcast flag (manual override wins) + resume state for one track.</summary>
    public PodcastPlaybackState GetPodcastState(string path)
    {
        using var command = _connection.CreateCommand();
        command.CommandText =
            "SELECT COALESCE(podcast_override, is_podcast), resume_position_ms, is_finished " +
            "FROM tracks WHERE path = $path";
        command.Parameters.AddWithValue("$path", path);
        using var reader = command.ExecuteReader();
        if (!reader.Read())
        {
            return PodcastPlaybackState.None;
        }

        return new PodcastPlaybackState(
            IsPodcast: reader.GetInt64(0) != 0,
            ResumePositionMs: reader.GetInt64(1),
            Finished: reader.GetInt64(2) != 0);
    }

    /// <summary>Persists where playback left off (podcast content only). Position 0 + finished = fully played.</summary>
    public void SaveResume(string path, long positionMs, bool finished)
    {
        lock (_writeLock)
        {
            using var command = _connection.CreateCommand();
            command.CommandText =
                "UPDATE tracks SET resume_position_ms = $pos, is_finished = $fin, " +
                "resume_updated_ticks = $now WHERE path = $path";
            command.Parameters.AddWithValue("$pos", Math.Max(0, positionMs));
            command.Parameters.AddWithValue("$fin", finished ? 1 : 0);
            command.Parameters.AddWithValue("$now", DateTime.UtcNow.Ticks);
            command.Parameters.AddWithValue("$path", path);
            command.ExecuteNonQuery();
        }
    }

    /// <summary>Sets (or with null, clears) the manual "this is / isn't a podcast" override.</summary>
    public void SetPodcastOverride(string path, bool? isPodcast)
    {
        lock (_writeLock)
        {
            using var command = _connection.CreateCommand();
            command.CommandText = "UPDATE tracks SET podcast_override = $val WHERE path = $path";
            command.Parameters.AddWithValue("$val", isPodcast is null ? DBNull.Value : isPodcast.Value ? 1 : 0);
            command.Parameters.AddWithValue("$path", path);
            command.ExecuteNonQuery();
        }
    }

    /// <summary>Every podcast/audiobook episode (auto-detected or manually flagged), for the Podcasts browser.</summary>
    public IReadOnlyList<PodcastEpisodeEntry> GetPodcastEpisodes()
    {
        var entries = new List<PodcastEpisodeEntry>();
        using var command = _connection.CreateCommand();
        command.CommandText =
            "SELECT path, title, artist, album, album_artist, genre, track_no, disc_no, year, " +
            "duration_ms, bitrate_kbps, sample_rate_hz, channels, file_size, format, bpm, musical_key, " +
            "resume_position_ms, is_finished, last_played " +
            "FROM tracks WHERE missing = 0 AND COALESCE(podcast_override, is_podcast) = 1 " +
            "ORDER BY album, disc_no, track_no, year, title";
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            entries.Add(new PodcastEpisodeEntry(
                ReadTrack(reader),
                ResumePositionMs: reader.GetInt64(17),
                Finished: reader.GetInt64(18) != 0,
                LastPlayed: reader.IsDBNull(19) ? null : new DateTime(reader.GetInt64(19), DateTimeKind.Utc)));
        }

        return entries;
    }

    /// <summary>Tracks plus their library stats, for browsing and smart playlist evaluation.</summary>
    public IReadOnlyList<LibraryEntry> GetAllEntries(bool includeMissing = false)
    {
        var entries = new List<LibraryEntry>();
        using var command = _connection.CreateCommand();
        command.CommandText =
            "SELECT path, title, artist, album, album_artist, genre, track_no, disc_no, year, " +
            "duration_ms, bitrate_kbps, sample_rate_hz, channels, file_size, format, bpm, musical_key, " +
            "play_count, date_added, last_played " +
            "FROM tracks" + (includeMissing ? string.Empty : " WHERE missing = 0") +
            " ORDER BY artist, album, disc_no, track_no, title";
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            entries.Add(new LibraryEntry(
                ReadTrack(reader),
                PlayCount: reader.GetInt32(17),
                DateAdded: new DateTime(reader.GetInt64(18), DateTimeKind.Utc),
                LastPlayed: reader.IsDBNull(19)
                    ? null
                    : new DateTime(reader.GetInt64(19), DateTimeKind.Utc)));
        }

        return entries;
    }

    /// <summary>Returns stored (mtimeTicks, fileSize) for incremental-scan change detection.</summary>
    public (long MtimeTicks, long FileSize)? GetFingerprint(string path)
    {
        using var command = _connection.CreateCommand();
        command.CommandText = "SELECT mtime_ticks, file_size FROM tracks WHERE path = $path";
        command.Parameters.AddWithValue("$path", path);
        using var reader = command.ExecuteReader();
        return reader.Read() ? (reader.GetInt64(0), reader.GetInt64(1)) : null;
    }

    public IReadOnlyList<TrackInfo> GetAllTracks(bool includeMissing = false)
    {
        var tracks = new List<TrackInfo>();
        using var command = _connection.CreateCommand();
        command.CommandText =
            "SELECT path, title, artist, album, album_artist, genre, track_no, disc_no, year, " +
            "duration_ms, bitrate_kbps, sample_rate_hz, channels, file_size, format, bpm, musical_key " +
            "FROM tracks" + (includeMissing ? string.Empty : " WHERE missing = 0") +
            " ORDER BY artist, album, disc_no, track_no, title";
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            tracks.Add(ReadTrack(reader));
        }

        return tracks;
    }

    public IReadOnlyList<string> GetAllPaths()
    {
        var paths = new List<string>();
        using var command = _connection.CreateCommand();
        command.CommandText = "SELECT path FROM tracks WHERE missing = 0";
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            paths.Add(reader.GetString(0));
        }

        return paths;
    }

    public void MarkMissing(string path)
    {
        lock (_writeLock)
        {
            using var command = _connection.CreateCommand();
            command.CommandText = "UPDATE tracks SET missing = 1 WHERE path = $path";
            command.Parameters.AddWithValue("$path", path);
            command.ExecuteNonQuery();
        }
    }

    public void Remove(string path)
    {
        lock (_writeLock)
        {
            using var command = _connection.CreateCommand();
            command.CommandText = "DELETE FROM tracks WHERE path = $path";
            command.Parameters.AddWithValue("$path", path);
            command.ExecuteNonQuery();
        }
    }

    public int Count()
    {
        using var command = _connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM tracks WHERE missing = 0";
        return Convert.ToInt32(command.ExecuteScalar());
    }

    private static TrackInfo ReadTrack(SqliteDataReader reader) =>
        new()
        {
            SourcePath = reader.GetString(0),
            Title = reader.GetString(1),
            Artist = reader.GetString(2),
            Album = reader.GetString(3),
            AlbumArtist = reader.GetString(4),
            Genre = reader.GetString(5),
            TrackNumber = (uint)reader.GetInt64(6),
            DiscNumber = (uint)reader.GetInt64(7),
            Year = (uint)reader.GetInt64(8),
            Duration = TimeSpan.FromMilliseconds(reader.GetInt64(9)),
            BitrateKbps = reader.GetInt32(10),
            SampleRateHz = reader.GetInt32(11),
            Channels = reader.GetInt32(12),
            FileSizeBytes = reader.GetInt64(13),
            FormatName = reader.GetString(14),
            Bpm = reader.IsDBNull(15) ? null : reader.GetDouble(15),
            MusicalKey = reader.IsDBNull(16) ? null : reader.GetString(16),
        };

    private void Execute(string sql)
    {
        using var command = _connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    public void Dispose() => _connection.Dispose();
}
