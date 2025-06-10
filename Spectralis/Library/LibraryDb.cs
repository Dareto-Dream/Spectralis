using System;
using System.Data;
using System.Data.SQLite;
using System.IO;

namespace Spectralis.Library
{
    public class LibraryDb : IDisposable
    {
        private readonly SQLiteConnection _connection;
        private readonly string _dbPath;

        public LibraryDb(string dbPath)
        {
            _dbPath = dbPath;
            Directory.CreateDirectory(Path.GetDirectoryName(dbPath));

            _connection = new SQLiteConnection($"Data Source={dbPath};Version=3;");
            _connection.Open();

            EnableWal();
            CreateSchema();
        }

        private void EnableWal()
        {
            using var cmd = new SQLiteCommand("PRAGMA journal_mode=WAL; PRAGMA synchronous=NORMAL;", _connection);
            cmd.ExecuteNonQuery();
        }

        private void CreateSchema()
        {
            using var tx = _connection.BeginTransaction();
            Execute(LibrarySchema.CreateTracks);
            Execute(LibrarySchema.CreateArtists);
            Execute(LibrarySchema.CreateAlbums);
            Execute(LibrarySchema.CreatePlaylistsTable);
            Execute(LibrarySchema.CreatePlaylistTracksTable);
            tx.Commit();
        }

        public SQLiteConnection Connection => _connection;

        public int Execute(string sql, params (string name, object value)[] parameters)
        {
            using var cmd = new SQLiteCommand(sql, _connection);
            foreach (var (name, value) in parameters)
                cmd.Parameters.AddWithValue(name, value ?? DBNull.Value);
            return cmd.ExecuteNonQuery();
        }

        public SQLiteDataReader Query(string sql, params (string name, object value)[] parameters)
        {
            var cmd = new SQLiteCommand(sql, _connection);
            foreach (var (name, value) in parameters)
                cmd.Parameters.AddWithValue(name, value ?? DBNull.Value);
            return cmd.ExecuteReader(CommandBehavior.CloseConnection);
        }

        public object QueryScalar(string sql, params (string name, object value)[] parameters)
        {
            using var cmd = new SQLiteCommand(sql, _connection);
            foreach (var (name, value) in parameters)
                cmd.Parameters.AddWithValue(name, value ?? DBNull.Value);
            return cmd.ExecuteScalar();
        }

        public SQLiteTransaction BeginTransaction() => _connection.BeginTransaction();

        public void Dispose()
        {
            _connection?.Close();
            _connection?.Dispose();
        }
    }
}
