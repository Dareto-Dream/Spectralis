namespace Spectralis.Library
{
    public static class LibrarySchema
    {
        public const string CreateTracks = @"
            CREATE TABLE IF NOT EXISTS tracks (
                id          INTEGER PRIMARY KEY AUTOINCREMENT,
                path        TEXT    NOT NULL UNIQUE,
                title       TEXT,
                artist      TEXT,
                album       TEXT,
                genre       TEXT,
                year        INTEGER,
                track_num   INTEGER,
                duration_ms INTEGER,
                bitrate     INTEGER,
                sample_rate INTEGER,
                channels    INTEGER,
                format      TEXT,
                cover_path  TEXT,
                date_added  TEXT    NOT NULL DEFAULT (datetime('now')),
                play_count  INTEGER NOT NULL DEFAULT 0,
                last_played TEXT
            )";

        public const string CreateArtists = @"
            CREATE TABLE IF NOT EXISTS artists (
                id   INTEGER PRIMARY KEY AUTOINCREMENT,
                name TEXT    NOT NULL UNIQUE
            )";

        public const string CreateAlbums = @"
            CREATE TABLE IF NOT EXISTS albums (
                id          INTEGER PRIMARY KEY AUTOINCREMENT,
                title       TEXT    NOT NULL,
                artist_id   INTEGER REFERENCES artists(id),
                year        INTEGER,
                cover_path  TEXT,
                UNIQUE(title, artist_id)
            )";

        public const string CreateFtsIndex = @"
            CREATE VIRTUAL TABLE IF NOT EXISTS tracks_fts USING fts5(
                title, artist, album, genre,
                content='tracks', content_rowid='id'
            )";

        public const string CreatePlaylistsTable = @"
            CREATE TABLE IF NOT EXISTS playlists (
                id          INTEGER PRIMARY KEY AUTOINCREMENT,
                name        TEXT    NOT NULL,
                created_at  TEXT    NOT NULL DEFAULT (datetime('now'))
            )";

        public const string CreatePlaylistTracksTable = @"
            CREATE TABLE IF NOT EXISTS playlist_tracks (
                playlist_id INTEGER NOT NULL REFERENCES playlists(id) ON DELETE CASCADE,
                track_id    INTEGER NOT NULL REFERENCES tracks(id)    ON DELETE CASCADE,
                position    INTEGER NOT NULL,
                PRIMARY KEY (playlist_id, track_id)
            )";
    }
}
