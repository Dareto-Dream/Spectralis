using System;
using System.Collections.Generic;
using System.Data.SQLite;

namespace Spectralis.Library
{
    public class LibraryRepository
    {
        private readonly LibraryDb _db;

        public LibraryRepository(LibraryDb db)
        {
            _db = db;
        }

        public void Upsert(LibraryTrack track)
        {
            _db.Execute(@"
                INSERT INTO tracks (path, title, artist, album, genre, year, track_num, duration_ms,
                    bitrate, sample_rate, channels, format, cover_path)
                VALUES (@path, @title, @artist, @album, @genre, @year, @trackNum, @durationMs,
                    @bitrate, @sampleRate, @channels, @format, @coverPath)
                ON CONFLICT(path) DO UPDATE SET
                    title=excluded.title, artist=excluded.artist, album=excluded.album,
                    genre=excluded.genre, year=excluded.year, track_num=excluded.track_num,
                    duration_ms=excluded.duration_ms, bitrate=excluded.bitrate,
                    sample_rate=excluded.sample_rate, channels=excluded.channels,
                    format=excluded.format, cover_path=excluded.cover_path",
                ("@path", track.Path), ("@title", track.Title), ("@artist", track.Artist),
                ("@album", track.Album), ("@genre", track.Genre), ("@year", track.Year),
                ("@trackNum", track.TrackNumber), ("@durationMs", track.DurationMs),
                ("@bitrate", track.Bitrate), ("@sampleRate", track.SampleRate),
                ("@channels", track.Channels), ("@format", track.Format), ("@coverPath", track.CoverPath));
        }

        public void Delete(string path)
        {
            _db.Execute("DELETE FROM tracks WHERE path=@path", ("@path", path));
        }

        public bool Exists(string path)
        {
            var result = _db.QueryScalar("SELECT COUNT(*) FROM tracks WHERE path=@path", ("@path", path));
            return Convert.ToInt64(result) > 0;
        }

        public IList<LibraryTrack> GetAll()
        {
            var tracks = new List<LibraryTrack>();
            using var reader = _db.Query("SELECT * FROM tracks ORDER BY artist, album, track_num");
            while (reader.Read())
                tracks.Add(MapTrack(reader));
            return tracks;
        }

        public IList<LibraryTrack> Search(string query)
        {
            var tracks = new List<LibraryTrack>();
            using var reader = _db.Query(@"
                SELECT t.* FROM tracks t
                JOIN tracks_fts f ON f.rowid = t.id
                WHERE tracks_fts MATCH @query
                ORDER BY rank",
                ("@query", query));
            while (reader.Read())
                tracks.Add(MapTrack(reader));
            return tracks;
        }

        public void IncrementPlayCount(int trackId)
        {
            _db.Execute(@"
                UPDATE tracks SET play_count = play_count + 1, last_played = datetime('now')
                WHERE id = @id", ("@id", trackId));
        }

        private static LibraryTrack MapTrack(SQLiteDataReader r)
        {
            return new LibraryTrack
            {
                Id = r.GetInt32(r.GetOrdinal("id")),
                Path = r.GetString(r.GetOrdinal("path")),
                Title = r.IsDBNull(r.GetOrdinal("title")) ? null : r.GetString(r.GetOrdinal("title")),
                Artist = r.IsDBNull(r.GetOrdinal("artist")) ? null : r.GetString(r.GetOrdinal("artist")),
                Album = r.IsDBNull(r.GetOrdinal("album")) ? null : r.GetString(r.GetOrdinal("album")),
                Genre = r.IsDBNull(r.GetOrdinal("genre")) ? null : r.GetString(r.GetOrdinal("genre")),
                Year = r.IsDBNull(r.GetOrdinal("year")) ? 0 : r.GetInt32(r.GetOrdinal("year")),
                TrackNumber = r.IsDBNull(r.GetOrdinal("track_num")) ? 0 : r.GetInt32(r.GetOrdinal("track_num")),
                DurationMs = r.IsDBNull(r.GetOrdinal("duration_ms")) ? 0 : r.GetInt64(r.GetOrdinal("duration_ms")),
                Bitrate = r.IsDBNull(r.GetOrdinal("bitrate")) ? 0 : r.GetInt32(r.GetOrdinal("bitrate")),
                SampleRate = r.IsDBNull(r.GetOrdinal("sample_rate")) ? 44100 : r.GetInt32(r.GetOrdinal("sample_rate")),
                Channels = r.IsDBNull(r.GetOrdinal("channels")) ? 2 : r.GetInt32(r.GetOrdinal("channels")),
                Format = r.IsDBNull(r.GetOrdinal("format")) ? null : r.GetString(r.GetOrdinal("format")),
                CoverPath = r.IsDBNull(r.GetOrdinal("cover_path")) ? null : r.GetString(r.GetOrdinal("cover_path")),
                DateAdded = DateTime.Parse(r.GetString(r.GetOrdinal("date_added"))),
                PlayCount = r.GetInt32(r.GetOrdinal("play_count"))
            };
        }
    }
}
