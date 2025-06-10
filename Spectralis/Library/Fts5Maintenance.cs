using System;

namespace Spectralis.Library
{
    public class Fts5Maintenance
    {
        private readonly LibraryDb _db;
        private DateTime _lastOptimize = DateTime.MinValue;
        private const int OptimizeIntervalHours = 24;

        public Fts5Maintenance(LibraryDb db)
        {
            _db = db;
        }

        public void RebuildTriggers()
        {
            _db.Execute("INSERT INTO tracks_fts(tracks_fts) VALUES('rebuild')");
        }

        public void Optimize()
        {
            if ((DateTime.UtcNow - _lastOptimize).TotalHours < OptimizeIntervalHours)
                return;

            _db.Execute("INSERT INTO tracks_fts(tracks_fts) VALUES('optimize')");
            _lastOptimize = DateTime.UtcNow;
        }

        public void Integrity()
        {
            using var reader = _db.Query("SELECT * FROM tracks_fts WHERE tracks_fts = 'integrity-check'");
            while (reader.Read())
            {
                var result = reader.GetString(0);
                if (result != "ok")
                    throw new InvalidOperationException($"FTS5 integrity check failed: {result}");
            }
        }
    }
}
