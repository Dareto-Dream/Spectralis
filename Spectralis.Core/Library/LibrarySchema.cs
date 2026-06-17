namespace Spectralis.Core.Library
{
    public static class LibrarySchema
    {
        public const int Version = 2;

        public const string CreateTracks = @"
CREATE TABLE IF NOT EXISTS tracks (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    file_path TEXT NOT NULL UNIQUE,
    title TEXT,
    artist TEXT,
    album_artist TEXT,
    album TEXT,
    genre TEXT,
    year INTEGER,
    track_number INTEGER,
    disc_number INTEGER,
    duration_ms INTEGER,
    file_size_bytes INTEGER,
    bitrate INTEGER,
    sample_rate INTEGER,
    cover_art_hash TEXT,
    date_added TEXT NOT NULL,
    date_modified TEXT
);";

        public const string CreateTracksIndex = @"
CREATE INDEX IF NOT EXISTS idx_tracks_artist ON tracks(artist);
CREATE INDEX IF NOT EXISTS idx_tracks_album ON tracks(album);
CREATE INDEX IF NOT EXISTS idx_tracks_genre ON tracks(genre);";

        public const string CreateFts = @"
CREATE VIRTUAL TABLE IF NOT EXISTS tracks_fts USING fts5(
    title, artist, album_artist, album, genre,
    content=tracks, content_rowid=id
);";

        public const string CreateFtsTriggers = @"
CREATE TRIGGER IF NOT EXISTS tracks_fts_insert AFTER INSERT ON tracks BEGIN
    INSERT INTO tracks_fts(rowid, title, artist, album_artist, album, genre)
    VALUES (new.id, new.title, new.artist, new.album_artist, new.album, new.genre);
END;
CREATE TRIGGER IF NOT EXISTS tracks_fts_delete AFTER DELETE ON tracks BEGIN
    INSERT INTO tracks_fts(tracks_fts, rowid, title, artist, album_artist, album, genre)
    VALUES ('delete', old.id, old.title, old.artist, old.album_artist, old.album, old.genre);
END;";

        public const string CreateMeta = @"
CREATE TABLE IF NOT EXISTS meta (key TEXT PRIMARY KEY, value TEXT);";
    }
}
