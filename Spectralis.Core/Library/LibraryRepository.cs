using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Spectralis.Core.Models;

namespace Spectralis.Core.Library
{
    public class LibraryRepository
    {
        private readonly LibraryDb _db;

        public LibraryRepository(LibraryDb db) => _db = db;

        public Task UpsertAsync(TrackInfo track, CancellationToken ct = default)
        {
            return Task.Run(() =>
            {
                const string sql = @"
INSERT INTO tracks(file_path,title,artist,album_artist,album,genre,year,track_number,disc_number,duration_ms,file_size_bytes,bitrate,sample_rate,date_added)
VALUES(@path,@title,@artist,@albumArtist,@album,@genre,@year,@trackNum,@discNum,@durMs,@size,@bitrate,@sr,@added)
ON CONFLICT(file_path) DO UPDATE SET
    title=excluded.title, artist=excluded.artist, album_artist=excluded.album_artist,
    album=excluded.album, genre=excluded.genre, year=excluded.year,
    duration_ms=excluded.duration_ms, file_size_bytes=excluded.file_size_bytes,
    date_modified=@modified;";

                using var cmd = _db.Connection.CreateCommand();
                cmd.CommandText = sql;
                cmd.Parameters.AddWithValue("@path", track.FilePath);
                cmd.Parameters.AddWithValue("@title", (object?)track.Title ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@artist", (object?)track.Artist ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@albumArtist", (object?)track.AlbumArtist ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@album", (object?)track.Album ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@genre", (object?)track.Genre ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@year", track.Year);
                cmd.Parameters.AddWithValue("@trackNum", track.TrackNumber);
                cmd.Parameters.AddWithValue("@discNum", track.DiscNumber);
                cmd.Parameters.AddWithValue("@durMs", (long)track.Duration.TotalMilliseconds);
                cmd.Parameters.AddWithValue("@size", track.FileSizeBytes);
                cmd.Parameters.AddWithValue("@bitrate", track.Bitrate);
                cmd.Parameters.AddWithValue("@sr", track.SampleRate);
                cmd.Parameters.AddWithValue("@added", DateTime.UtcNow.ToString("o"));
                cmd.Parameters.AddWithValue("@modified", DateTime.UtcNow.ToString("o"));
                cmd.ExecuteNonQuery();
            }, ct);
        }

        public Task<IReadOnlyList<TrackInfo>> SearchAsync(string query, CancellationToken ct = default)
        {
            return Task.Run<IReadOnlyList<TrackInfo>>(() =>
            {
                string term = query.Trim().Replace("\"", "\\\"") + "*";
                const string sql = @"
SELECT t.file_path,t.title,t.artist,t.album_artist,t.album,t.genre,t.year,t.track_number,t.disc_number,t.duration_ms,t.bitrate,t.sample_rate,t.file_size_bytes
FROM tracks t
JOIN tracks_fts f ON t.id = f.rowid
WHERE tracks_fts MATCH @term
LIMIT 500;";

                var result = new List<TrackInfo>();
                using var cmd = _db.Connection.CreateCommand();
                cmd.CommandText = sql;
                cmd.Parameters.AddWithValue("@term", term);
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                    result.Add(ReadTrack(reader));
                return result;
            }, ct);
        }

        public Task<IReadOnlyList<TrackInfo>> GetAllAsync(CancellationToken ct = default)
        {
            return Task.Run<IReadOnlyList<TrackInfo>>(() =>
            {
                const string sql = "SELECT file_path,title,artist,album_artist,album,genre,year,track_number,disc_number,duration_ms,bitrate,sample_rate,file_size_bytes FROM tracks ORDER BY artist,album,track_number;";
                var result = new List<TrackInfo>();
                using var cmd = _db.Connection.CreateCommand();
                cmd.CommandText = sql;
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                    result.Add(ReadTrack(reader));
                return result;
            }, ct);
        }

        public Task DeleteAsync(string filePath, CancellationToken ct = default)
        {
            return Task.Run(() =>
            {
                using var cmd = _db.Connection.CreateCommand();
                cmd.CommandText = "DELETE FROM tracks WHERE file_path=@path;";
                cmd.Parameters.AddWithValue("@path", filePath);
                cmd.ExecuteNonQuery();
            }, ct);
        }

        public Task<int> CountAsync(CancellationToken ct = default)
        {
            return Task.Run(() =>
            {
                using var cmd = _db.Connection.CreateCommand();
                cmd.CommandText = "SELECT COUNT(*) FROM tracks;";
                return Convert.ToInt32(cmd.ExecuteScalar());
            }, ct);
        }

        private static TrackInfo ReadTrack(SqliteDataReader r)
        {
            return new TrackInfo
            {
                FilePath = r.GetString(0),
                Title = r.IsDBNull(1) ? null : r.GetString(1),
                Artist = r.IsDBNull(2) ? null : r.GetString(2),
                AlbumArtist = r.IsDBNull(3) ? null : r.GetString(3),
                Album = r.IsDBNull(4) ? null : r.GetString(4),
                Genre = r.IsDBNull(5) ? null : r.GetString(5),
                Year = r.IsDBNull(6) ? 0 : r.GetInt32(6),
                TrackNumber = r.IsDBNull(7) ? 0 : r.GetInt32(7),
                DiscNumber = r.IsDBNull(8) ? 0 : r.GetInt32(8),
                Duration = TimeSpan.FromMilliseconds(r.IsDBNull(9) ? 0 : r.GetInt64(9)),
                Bitrate = r.IsDBNull(10) ? 0 : r.GetInt32(10),
                SampleRate = r.IsDBNull(11) ? 0 : r.GetInt32(11),
                FileSizeBytes = r.IsDBNull(12) ? 0 : r.GetInt64(12)
            };
        }
    }
}
