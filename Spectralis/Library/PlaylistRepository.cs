using System;
using System.Collections.Generic;
using System.Data.SQLite;

namespace Spectralis.Library
{
    public class DbPlaylist
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public DateTime CreatedAt { get; set; }
        public List<LibraryTrack> Tracks { get; set; } = new List<LibraryTrack>();
    }

    public class PlaylistRepository
    {
        private readonly LibraryDb _db;
        private readonly LibraryRepository _trackRepo;

        public PlaylistRepository(LibraryDb db, LibraryRepository trackRepo)
        {
            _db = db;
            _trackRepo = trackRepo;
        }

        public int Create(string name)
        {
            _db.Execute("INSERT INTO playlists (name) VALUES (@name)", ("@name", name));
            return (int)(long)_db.QueryScalar("SELECT last_insert_rowid()");
        }

        public void Rename(int id, string newName)
        {
            _db.Execute("UPDATE playlists SET name=@name WHERE id=@id", ("@name", newName), ("@id", id));
        }

        public void Delete(int id)
        {
            _db.Execute("DELETE FROM playlists WHERE id=@id", ("@id", id));
        }

        public void AddTrack(int playlistId, int trackId, int position)
        {
            _db.Execute(@"
                INSERT OR REPLACE INTO playlist_tracks (playlist_id, track_id, position)
                VALUES (@pid, @tid, @pos)",
                ("@pid", playlistId), ("@tid", trackId), ("@pos", position));
        }

        public void RemoveTrack(int playlistId, int trackId)
        {
            _db.Execute("DELETE FROM playlist_tracks WHERE playlist_id=@pid AND track_id=@tid",
                ("@pid", playlistId), ("@tid", trackId));
        }

        public IList<DbPlaylist> GetAll()
        {
            var result = new List<DbPlaylist>();
            using var r = _db.Query("SELECT id, name, created_at FROM playlists ORDER BY name");
            while (r.Read())
                result.Add(new DbPlaylist
                {
                    Id = r.GetInt32(0),
                    Name = r.GetString(1),
                    CreatedAt = DateTime.Parse(r.GetString(2))
                });
            return result;
        }
    }
}
