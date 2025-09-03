using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Spectralis.Core.Library;

namespace Spectralis.Core.Playlists
{
    public class PlaylistRepository
    {
        private readonly LibraryDb _db;
        private static readonly JsonSerializerOptions _opts = new();

        public PlaylistRepository(LibraryDb db)
        {
            _db = db;
            EnsureSchema();
        }

        private void EnsureSchema()
        {
            _db.Execute(@"
                CREATE TABLE IF NOT EXISTS playlists (
                    id TEXT PRIMARY KEY,
                    name TEXT NOT NULL,
                    is_smart INTEGER NOT NULL DEFAULT 0,
                    rules_json TEXT NOT NULL DEFAULT '[]',
                    match_all INTEGER NOT NULL DEFAULT 1,
                    sort_by TEXT NOT NULL DEFAULT 'artist',
                    lim INTEGER,
                    created_at TEXT NOT NULL,
                    updated_at TEXT NOT NULL
                );
                CREATE TABLE IF NOT EXISTS playlist_tracks (
                    playlist_id TEXT NOT NULL,
                    file_path TEXT NOT NULL,
                    position INTEGER NOT NULL DEFAULT 0,
                    added_at TEXT NOT NULL,
                    PRIMARY KEY (playlist_id, file_path)
                );");
        }

        public async Task UpsertAsync(Playlist pl)
        {
            string rulesJson = JsonSerializer.Serialize(pl.Rules, _opts);
            await Task.Run(() => _db.Execute(@"
                INSERT INTO playlists (id,name,is_smart,rules_json,match_all,sort_by,lim,created_at,updated_at)
                VALUES (@id,@name,@is,@rules,@ma,@sb,@lim,@ca,@ua)
                ON CONFLICT(id) DO UPDATE SET
                    name=excluded.name,is_smart=excluded.is_smart,rules_json=excluded.rules_json,
                    match_all=excluded.match_all,sort_by=excluded.sort_by,lim=excluded.lim,
                    updated_at=excluded.updated_at",
                new { id = pl.Id.ToString(), name = pl.Name, is_ = pl.IsSmart ? 1 : 0,
                      rules = rulesJson, ma = pl.MatchAll ? 1 : 0, sb = pl.SortBy,
                      lim = (object?)pl.Limit ?? DBNull.Value,
                      ca = pl.CreatedAt.ToString("o"), ua = pl.UpdatedAt.ToString("o") }));
        }

        public async Task<List<Playlist>> GetAllAsync()
        {
            var result = new List<Playlist>();
            await Task.Run(() =>
            {
                using var conn = _db.OpenConnection();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT * FROM playlists ORDER BY name";
                using var r = cmd.ExecuteReader();
                while (r.Read())
                {
                    var pl = new Playlist
                    {
                        Id = Guid.Parse(r.GetString(r.GetOrdinal("id"))),
                        Name = r.GetString(r.GetOrdinal("name")),
                        IsSmart = r.GetInt32(r.GetOrdinal("is_smart")) == 1,
                        MatchAll = r.GetInt32(r.GetOrdinal("match_all")) == 1,
                        SortBy = r.GetString(r.GetOrdinal("sort_by")),
                        CreatedAt = DateTime.Parse(r.GetString(r.GetOrdinal("created_at"))),
                        UpdatedAt = DateTime.Parse(r.GetString(r.GetOrdinal("updated_at")))
                    };
                    string rulesJson = r.GetString(r.GetOrdinal("rules_json"));
                    pl.Rules = JsonSerializer.Deserialize<List<SmartPlaylistRule>>(rulesJson, _opts) ?? new();
                    int limOrd = r.GetOrdinal("lim");
                    if (!r.IsDBNull(limOrd)) pl.Limit = r.GetInt32(limOrd);
                    result.Add(pl);
                }
            });
            return result;
        }

        public async Task DeleteAsync(Guid id)
        {
            string sid = id.ToString();
            await Task.Run(() =>
            {
                _db.Execute("DELETE FROM playlist_tracks WHERE playlist_id=@id", new { id = sid });
                _db.Execute("DELETE FROM playlists WHERE id=@id", new { id = sid });
            });
        }

        public async Task AddTrackAsync(Guid playlistId, string filePath, int position)
        {
            await Task.Run(() => _db.Execute(
                "INSERT OR IGNORE INTO playlist_tracks (playlist_id,file_path,position,added_at) VALUES (@pid,@fp,@pos,@at)",
                new { pid = playlistId.ToString(), fp = filePath, pos = position, at = DateTime.UtcNow.ToString("o") }));
        }

        public async Task RemoveTrackAsync(Guid playlistId, string filePath)
        {
            await Task.Run(() => _db.Execute(
                "DELETE FROM playlist_tracks WHERE playlist_id=@pid AND file_path=@fp",
                new { pid = playlistId.ToString(), fp = filePath }));
        }

        public async Task<List<string>> GetTrackPathsAsync(Guid playlistId)
        {
            var paths = new List<string>();
            await Task.Run(() =>
            {
                using var conn = _db.OpenConnection();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT file_path FROM playlist_tracks WHERE playlist_id=@pid ORDER BY position";
                cmd.Parameters.AddWithValue("@pid", playlistId.ToString());
                using var r = cmd.ExecuteReader();
                while (r.Read()) paths.Add(r.GetString(0));
            });
            return paths;
        }
    }
}
