using System;
using System.Data;
using Microsoft.Data.Sqlite;

namespace Spectralis.Core.Library
{
    public class LibraryDb : IDisposable
    {
        private readonly SqliteConnection _conn;
        private bool _disposed;

        public LibraryDb(string dbPath)
        {
            _conn = new SqliteConnection($"Data Source={dbPath}");
            _conn.Open();
            Execute("PRAGMA journal_mode=WAL;");
            Execute("PRAGMA synchronous=NORMAL;");
            Execute("PRAGMA temp_store=MEMORY;");
            Migrate();
        }

        private void Migrate()
        {
            Execute(LibrarySchema.CreateTracks);
            Execute(LibrarySchema.CreateTracksIndex);
            Execute(LibrarySchema.CreateFts);
            Execute(LibrarySchema.CreateFtsTriggers);
            Execute(LibrarySchema.CreateMeta);
            Execute($"INSERT OR REPLACE INTO meta(key,value) VALUES('schema_version','{LibrarySchema.Version}');");
        }

        public SqliteConnection Connection => _conn;

        public void Execute(string sql)
        {
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = sql;
            cmd.ExecuteNonQuery();
        }

        public SqliteTransaction BeginTransaction() => _conn.BeginTransaction();

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _conn.Dispose();
        }
    }
}
