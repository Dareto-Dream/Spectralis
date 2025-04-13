using System.Collections.Generic;
using System.Data.SQLite;

namespace Spectralis.Library
{
    public class RecentlyPlayedQuery
    {
        private readonly LibraryDb _db;
        private readonly LibraryRepository _repo;

        public RecentlyPlayedQuery(LibraryDb db, LibraryRepository repo)
        {
            _db = db;
            _repo = repo;
        }

        public IList<LibraryTrack> GetRecentlyPlayed(int limit = 25)
        {
            var tracks = new List<LibraryTrack>();
            using var reader = _db.Query(
                "SELECT * FROM tracks WHERE last_played IS NOT NULL ORDER BY last_played DESC LIMIT @limit",
                ("@limit", limit));
            while (reader.Read())
                tracks.Add(MapTrack(reader));
            return tracks;
        }

        public IList<LibraryTrack> GetMostPlayed(int limit = 25)
        {
            var tracks = new List<LibraryTrack>();
            using var reader = _db.Query(
                "SELECT * FROM tracks WHERE play_count > 0 ORDER BY play_count DESC LIMIT @limit",
                ("@limit", limit));
            while (reader.Read())
                tracks.Add(MapTrack(reader));
            return tracks;
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
                PlayCount = r.GetInt32(r.GetOrdinal("play_count")),
                DurationMs = r.IsDBNull(r.GetOrdinal("duration_ms")) ? 0 : r.GetInt64(r.GetOrdinal("duration_ms"))
            };
        }
    }
}
