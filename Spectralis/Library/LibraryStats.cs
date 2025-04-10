using System;
using System.Data.SQLite;

namespace Spectralis.Library
{
    public class LibraryStats
    {
        public int TotalTracks { get; set; }
        public long TotalDurationMs { get; set; }
        public int UniqueArtists { get; set; }
        public int UniqueAlbums { get; set; }
        public long TotalSizeBytes { get; set; }

        public TimeSpan TotalDuration => TimeSpan.FromMilliseconds(TotalDurationMs);

        public static LibraryStats Compute(LibraryDb db)
        {
            var stats = new LibraryStats();

            using (var r = db.Query("SELECT COUNT(*), SUM(duration_ms) FROM tracks"))
                if (r.Read()) { stats.TotalTracks = r.GetInt32(0); stats.TotalDurationMs = r.IsDBNull(1) ? 0 : r.GetInt64(1); }

            using (var r = db.Query("SELECT COUNT(DISTINCT artist) FROM tracks WHERE artist IS NOT NULL"))
                if (r.Read()) stats.UniqueArtists = r.GetInt32(0);

            using (var r = db.Query("SELECT COUNT(DISTINCT album) FROM tracks WHERE album IS NOT NULL"))
                if (r.Read()) stats.UniqueAlbums = r.GetInt32(0);

            return stats;
        }
    }
}
