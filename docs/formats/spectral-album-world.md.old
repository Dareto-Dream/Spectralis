# Album World Format (`.spectral`)

`.spectral` is Spectralis's multi-track album capsule format. Where a `.spectralis` capsule
ships one track with one optional experience, a `.spectral` capsule ships an entire album with
an interactive HTML "world" that the creator controls.

Examples of what the world can be: a Super Mario World-style level map where each song is a
level, an interactive liner notes page, a branching narrative, or any custom HTML experience the
creator builds. If the creator ships no world, the player falls back to a simple tracklist.

For the single-track format, see [spectralis-capsule.md](spectralis-capsule.md).

---

## Binary Container

The binary envelope is **identical to `.spectralis`**, with one exception: different magic bytes.

```
[4]   Magic bytes: 0x53 0x50 0x41 0x43  ('SPAC')
[4]   Format version: 1  (Int32 little-endian)
[32]  Ed25519 public key  (creator signing key)
[64]  Ed25519 signature over all bytes that follow
[n]   Payload: a standard ZIP archive
```

- **Fingerprint** = `SHA256(public_key_bytes)` as a lowercase hex string.
- **Signature** covers the raw ZIP payload bytes only (everything after the 104-byte header).
- Header layout: bytes 0–3 magic, 4–7 version, 8–39 pubkey, 40–103 signature, 104+ payload.

This means the same signing tool, CDN key infrastructure, and trust dialog used for `.spectralis`
capsules work unchanged for `.spectral` capsules.

---

## Trust Model

Trust check mirrors the `.spectralis` flow exactly:

1. `AlbumCapsuleReader.Read(path)` validates SPAC magic, version 1, and the Ed25519 signature.
2. `CapsuleCdnClient.FetchCreatorKeyAsync(fingerprint)` — `GET /spectralis/keys/{fingerprint}.json`.
   Falls back to `CreatorTrustStore` cache on network failure.
3. Reject if key is 404, `status` is not `active`, or `revokedAtUtc` is set.
4. Intersect `manifest.capabilities` with `keyMetadata.allowedCapabilities`. Reject if any
   requested capability is missing from the CDN key.
5. Check `CreatorTrustStore.IsTrusted(fingerprint)`. If not trusted, show `CreatorTrustDialog`.
6. Cache updated metadata; trust the fingerprint on first approval.

The shared `CreatorTrustStore` persists both `.spectralis` and `.spectral` trust decisions at
`%LocalAppData%\Spectralis\trusted-creators.json`. A creator trusted for a single-track capsule
is not automatically trusted for an album capsule — both formats intersect against the CDN key's
`allowedCapabilities` independently.

### Required Capability

Album capsules **must** declare `"album.world"` in `manifest.capabilities`, and the CDN key must
include it in `allowedCapabilities`. A capsule without this capability is rejected.

---

## Manifest Schema

Format name: `spectralis-album`, version: `1`.

```json
{
  "format": "spectralis-album",
  "formatVersion": 1,
  "id": "my-album-2026",
  "title": "Album Title",
  "artist": "Artist Name",
  "release": {
    "year": 2026,
    "credits": []
  },
  "signature": {
    "keyId": "creator-key-id",
    "fingerprint": "sha256-hex",
    "algorithm": "Ed25519",
    "value": "base64-sig"
  },
  "capabilities": ["webview.localContent", "album.world"],
  "story": { },
  "world": {
    "entry": "world/index.html",
    "binaryAssets": {
      "bg": "world/assets/bg.webp"
    },
    "dataAssets": {
      "config": "world/config.json"
    }
  },
  "tracks": [
    {
      "id": "track-01",
      "title": "Track One",
      "artist": "Artist Name",
      "audio": {
        "entry": "tracks/01/audio.flac",
        "sha256": "lowercase-hex-sha256",
        "durationSeconds": 210.5
      },
      "assets": {
        "images": ["tracks/01/cover.png"],
        "data": ["tracks/01/lyrics.lrc"]
      },
      "visualizers": [],
      "timeline": [],
      "suppressAppLyrics": false
    }
  ]
}
```

### Top-Level Fields

| Field | Required | Description |
|---|---|---|
| `format` | Yes | Must be `"spectralis-album"` |
| `formatVersion` | Yes | Must be `1` |
| `id` | Yes | Unique album ID, alphanumeric + dash/underscore/dot, max 64 chars |
| `title` | Yes | Album display title |
| `artist` | Yes | Artist name |
| `release` | No | `CapsuleRelease` — year, credits, etc. |
| `signature` | Yes | `CapsuleSignatureBlock` — keyId, fingerprint, algorithm, value |
| `capabilities` | Yes | List of capability strings requested by the capsule |
| `story` | No | `CapsuleStory` — optional intro explainer (reuses single-track story schema) |
| `world` | No | `AlbumWorldSection` — HTML entry point and assets |
| `tracks` | Yes | Array of `AlbumTrackEntry` objects |

---

## Album World Section

```json
"world": {
  "entry": "world/index.html",
  "binaryAssets": { },
  "dataAssets": { }
}
```

| Field | Required | Description |
|---|---|---|
| `entry` | Yes (if section present) | Path within ZIP to the HTML file that boots the world |
| `binaryAssets` | No | Named binary assets (images, fonts) available at the virtual host |
| `dataAssets` | No | Named data assets (JSON configs, manifests) |

**`world` is optional.** If absent or if `entry` does not resolve to an existing file in the
extracted album directory, the player shows the fallback tracklist UI instead of loading WebView2.

### How the World is Served

The extracted `world/` folder (or the album root if no `world/` subfolder exists) is mounted as a
WebView2 virtual host:

```
spectral-world.local  →  extracted world folder
```

The world entry point loads at `https://spectral-world.local/{entryFile}`. This means all
relative paths in the HTML (`./assets/bg.webp`, `../data/config.json`) resolve normally. No
CORS issues. No base-64 data URI workarounds.

The virtual host is configured with `DenyCors` to prevent cross-origin requests from inside the
world page.

---

## Track Entries

Each entry in `tracks[]` describes one audio track and its per-track assets.

```json
{
  "id": "track-01",
  "title": "Track One",
  "artist": "Artist Name",
  "audio": {
    "entry": "tracks/01/audio.flac",
    "sha256": "lowercase-hex-sha256",
    "durationSeconds": 210.5
  },
  "assets": {
    "images": ["tracks/01/cover.png"],
    "data": ["tracks/01/lyrics.lrc"]
  },
  "visualizers": [
    {
      "type": "html",
      "binaryEntry": "tracks/01/viz.html"
    }
  ],
  "timeline": [],
  "suppressAppLyrics": false
}
```

| Field | Required | Description |
|---|---|---|
| `id` | Yes | Unique track ID within this album (referenced by the JS API) |
| `title` | Yes | Track display title |
| `artist` | No | Track artist (falls back to album `artist`) |
| `audio.entry` | Yes | Path within ZIP to the audio file |
| `audio.sha256` | No | SHA-256 of the audio bytes for integrity checks |
| `audio.durationSeconds` | No | Duration hint used by the JS API state |
| `assets.images` | No | Cover art paths; first PNG used as album art |
| `assets.data` | No | Data file paths; first `.lrc` loaded as synced lyrics |
| `visualizers` | No | Per-track HTML/WASM visualizer descriptors (same schema as embedded modules) |
| `timeline` | No | Per-track reactive timeline events |
| `suppressAppLyrics` | No | `true` to hide the app's lyrics panel (when the visualizer renders lyrics) |

Per-track capabilities match those of a single-track `.spectralis` capsule: LRC lyrics, cover
art, HTML/WASM visualizer, and reactive timeline are all supported.

---

## Cache and Session Storage

### Directory Structure

```
%LOCALAPPDATA%\Spectralis\AlbumWorlds\
  {albumId}\
    _meta.json          ← cache metadata
    session.json        ← playback session state
    tracks\
      {trackId}\
        audio.{ext}
        lyrics.lrc          (optional)
        viz.html            (optional HTML visualizer)
        reactive.json       (optional reactive timeline)
    world\
      index.html
      assets\             (creator world assets)
```

### `_meta.json`

```json
{
  "albumId": "my-album-2026",
  "extractedAtUtc": "2026-05-01T12:00:00Z",
  "lastPlayedUtc": "2026-05-20T15:30:00Z",
  "pinned": false,
  "sourceFingerprint": "sha256-hex-of-creator-pubkey"
}
```

### `session.json`

```json
{
  "albumId": "my-album-2026",
  "currentTrackId": "track-01",
  "currentPositionSeconds": 45.2,
  "lastPlayedUtc": "2026-05-20T15:30:00Z",
  "introCompleted": true,
  "trackStats": {
    "track-01": {
      "playedSeconds": 180.0,
      "completed": true,
      "lastPlayedUtc": "2026-05-20T15:30:00Z"
    }
  },
  "bookmarks": [
    {
      "trackId": "track-01",
      "positionSeconds": 92.0,
      "label": "chorus",
      "createdUtc": "2026-05-20T15:28:00Z"
    }
  ]
}
```

### Cache Lifetime

- Albums are cached for **30 days** from `lastPlayedUtc`.
- `lastPlayedUtc` is updated every time a track is played (`TouchAccess`).
- Albums with `pinned: true` are never deleted by the cleanup pass.
- `CleanupExpired()` runs on every album open — it reads all `_meta.json` files in the root
  and deletes directories where `lastPlayedUtc` is older than 30 days and `pinned` is false.

### Pinning

The user can pin an album via **File → Save Album**. This sets `pinned: true` in `_meta.json`
and prevents automatic cleanup.

### Extraction

Extraction is idempotent. If `_meta.json` already exists for an album, the ZIP is not
re-extracted. To force re-extraction, delete the album's directory in `AlbumWorlds\`.

---

## JavaScript API

The world HTML page communicates with Spectralis through `window.spectral`. All callbacks are
stubbed as no-ops before the page navigates, so the world never needs null checks.

### C# → World (callbacks)

Called by Spectralis via `ExecuteScriptAsync`.

#### `window.spectral.onReady(state)`

Fired once after the world page finishes loading and the bootstrap has been injected. `state` is
the full `AlbumWorldState` object.

```json
{
  "albumId": "my-album-2026",
  "title": "Album Title",
  "artist": "Artist Name",
  "tracks": [
    {
      "id": "track-01",
      "title": "Track One",
      "artist": "Artist Name",
      "durationSeconds": 210.5
    }
  ],
  "session": {
    "currentTrackId": "track-01",
    "currentPositionSeconds": 45.2,
    "introCompleted": true,
    "trackStats": {
      "track-01": {
        "playedSeconds": 180.0,
        "completed": true
      }
    }
  }
}
```

#### `window.spectral.onTrackChanged(info)`

Fired when a track starts playing.

```json
{
  "id": "track-01",
  "title": "Track One",
  "artist": "Artist Name",
  "durationSeconds": 210.5
}
```

#### `window.spectral.onPlaybackFrame(frame)`

Fired approximately every 33 ms during active playback.

```json
{
  "levels": [0.42, 0.31, 0.55],
  "peak": 0.78,
  "rms": 0.44,
  "time": 45.2,
  "active": true,
  "trackId": "track-01"
}
```

- `levels` — frequency band magnitudes (length varies by frame)
- `peak` — instantaneous peak level (0–1)
- `rms` — root mean square level (0–1)
- `time` — current playback position in seconds
- `active` — `false` when paused or stopped
- `trackId` — currently playing track ID, or `null`

#### `window.spectral.onTrackCompleted(trackId, stats)`

Fired when a track reaches its natural end.

```json
{
  "trackId": "track-01",
  "playedSeconds": 210.5
}
```

The world decides what happens next — auto-advance, show completion screen, etc.

### World → C# (messages)

Posted by the world via `window.chrome.webview.postMessage(JSON.stringify({ type, ...payload }))`.

#### `spectral.playTrack`

Start playing a track, optionally at a specific position.

```json
{ "type": "spectral.playTrack", "trackId": "track-01", "positionSeconds": 0 }
```

`positionSeconds` is optional (defaults to 0).

#### `spectral.addToQueue`

Append a track to the playback queue (continues after the current track).

```json
{ "type": "spectral.addToQueue", "trackId": "track-02" }
```

#### `spectral.pause` / `spectral.resume`

Pause or resume the current track.

```json
{ "type": "spectral.pause" }
{ "type": "spectral.resume" }
```

#### `spectral.seek`

Seek the current track to a position in seconds.

```json
{ "type": "spectral.seek", "positionSeconds": 92.0 }
```

#### `spectral.saveBookmark`

Save a named bookmark at the current or given position.

```json
{
  "type": "spectral.saveBookmark",
  "trackId": "track-01",
  "positionSeconds": 92.0,
  "label": "chorus"
}
```

The bookmark is written to `session.json` immediately.

#### `spectral.exitWorld`

Return to the normal player UI, unloading the album world.

```json
{ "type": "spectral.exitWorld" }
```

---

## Bootstrap Script Injection

Before the world page finishes loading (on `ContentLoading`), Spectralis injects a bootstrap
script that stubs all `window.spectral.*` callbacks as empty functions. This prevents the world
page from crashing if Spectralis hasn't called `onReady` yet.

After the page finishes loading, Spectralis injects:

1. The full bootstrap stubs again (idempotent).
2. A call to `window.spectral.onReady(stateJson)` with the current album and session state.

The 200 ms delay between page load and injection is intentional — it waits for the page's own
JavaScript to finish initializing before delivering the ready event.

---

## Fallback Tracklist UI

When `manifest.world` is absent, or when `world.entry` does not resolve to an existing file in
the extracted album directory, the player shows `AlbumWorldFallbackControl` instead of WebView2.

The fallback control shows:

- Album title and artist.
- A scrollable list of all tracks.
- A checkmark prefix (`✓`) on tracks where `session.trackStats[id].completed` is true.
- Clicking a track starts playback immediately.

The fallback uses the active `ThemePalette` and updates when the session changes.

---

## Opening Flow

1. User drops a `.spectral` file or opens it via File → Open Audio.
2. `IsAlbumCapsulePath(path)` returns true (`.spectral` extension); routed to
   `OpenAlbumCapsuleAsync`.
3. `AlbumCapsuleReader.Read(path)` validates the binary container.
4. Trust check via `CapsuleCdnClient` + `CreatorTrustStore` (see [Trust Model](#trust-model)).
5. `AlbumWorldCacheStore.CleanupExpired()` runs, removing any stale non-pinned albums.
6. `AlbumWorldCacheStore.GetOrExtract(package)` extracts the ZIP if needed.
7. `AlbumWorldSessionStore.Load(albumDir)` reads `session.json` (or returns an empty session).
8. **Intro story check:** if `manifest.story` has explainer tags and `session.introCompleted`
   is false, the `CapsuleStoryControl` is shown. `StoryCompleted` fires `CompleteAlbumWorldIntro`.
9. `ShowAlbumWorld()` is called (either after the story or directly).
10. If `world.entry` exists: `embeddedContentControl.LoadWorldContent(folder, entry)` → bootstrap
    injection → `onReady(state)` delivered.
11. If `world.entry` is absent: `albumWorldFallbackControl.LoadAlbum(manifest, session)`.

---

## Timer Integration

`TickAlbumWorld()` is called from `timer1_Tick` (33 ms interval) alongside `AdvanceReactive()`.

Each tick:
- Calls `albumWorldRuntime.Tick(position, playing)` to accumulate `trackStats.playedSeconds`.
- Detects track completion: `prevEngineIsPlaying && !playing && engine.IsLoaded && reachedEnd`.
- On completion: calls `NotifyTrackCompleted`, saves the session, and fires
  `PushTrackCompletedToWorldAsync` (which delivers `onTrackCompleted` via `ExecuteScriptAsync`).

`SyncAlbumWorldFrameAsync` is called from `UpdateVisualizer()` and delivers `onPlaybackFrame`
to the world via the rate-limited `SyncWorldFrame` helper (33 ms minimum between frames).

The normal queue's `CheckAutoAdvance()` is guarded by `!IsAlbumWorldActive`, so it does not
advance to the next queue item while an album world is loaded.

---

## Session Persistence

The session is saved:

- On each `PlayAlbumTrack` call.
- When a track reaches its natural end.
- On `OnFormClosed`.

`AlbumWorldRuntime.SaveSession()` calls `AlbumWorldSessionStore.Save(albumDir, session)`, which
writes `session.json` atomically (write to temp file, rename).

`session.introCompleted` is set to `true` when `CompleteAlbumWorldIntro()` is called, so the
intro story is never shown again for this album.

---

## Creator Guide

### Minimal Album World Structure

```
manifest.json
tracks/
  track-01/
    audio.flac
    cover.png
    lyrics.lrc
  track-02/
    audio.mp3
world/
  index.html
  assets/
    map.webp
    theme.css
```

Sign and package with the same signing tool used for `.spectralis` capsules. The only differences
are:
- Use SPAC magic bytes (`0x53 0x50 0x41 0x43`).
- Set `format: "spectralis-album"`, `formatVersion: 1`.
- Include `"album.world"` in `capabilities` (and ensure the CDN key grants it).

### Building the World Page

The world page is a standard HTML + CSS + JS application. It has full access to modern browser
APIs because it runs in a real WebView2 context with a virtual host origin
(`https://spectral-world.local`), not in a sandboxed iframe.

**Minimum setup:**

```html
<!DOCTYPE html>
<html>
<head>
  <meta charset="utf-8">
  <title>My Album World</title>
</head>
<body>
  <script>
    // All window.spectral.* callbacks are pre-stubbed. You can override them:
    window.spectral.onReady = function(state) {
      // state.tracks — array of { id, title, artist, durationSeconds }
      // state.session — { currentTrackId, currentPositionSeconds, trackStats }
      renderTrackList(state.tracks, state.session);
    };

    window.spectral.onTrackChanged = function(info) {
      highlightTrack(info.id);
    };

    window.spectral.onPlaybackFrame = function(frame) {
      updateProgress(frame.time, frame.active);
    };

    window.spectral.onTrackCompleted = function(trackId, stats) {
      markCompleted(trackId);
      // Decide what to play next, or let the listener choose.
    };

    function playTrack(trackId) {
      window.chrome.webview.postMessage(
        JSON.stringify({ type: 'spectral.playTrack', trackId })
      );
    }
  </script>
</body>
</html>
```

### Track Stats

Use `state.session.trackStats` in `onReady` to show which tracks have been played or completed.
Use `stats.playedSeconds` to show how much of a track has been heard (useful for percentage
completion indicators or unlock mechanics).

### Bookmarks

Your world can create bookmarks for special moments:

```js
window.chrome.webview.postMessage(JSON.stringify({
  type: 'spectral.saveBookmark',
  trackId: 'track-01',
  positionSeconds: 92.0,
  label: 'chorus'
}));
```

Bookmarks survive in `session.json`. You can read them from `state.session.bookmarks` in `onReady`
and use them to let the listener jump back to key moments.

### Exit

Provide an exit button so the listener can return to the normal Spectralis player:

```js
document.getElementById('exit-btn').addEventListener('click', () => {
  window.chrome.webview.postMessage(JSON.stringify({ type: 'spectral.exitWorld' }));
});
```

### Notes

- The world origin is `https://spectral-world.local`. Relative paths in HTML and CSS resolve
  against this origin. Absolute paths (`/assets/bg.webp`) also work.
- The world page has network access to the origin only — it cannot make requests to external URLs
  unless the creator explicitly requests the `webview.networkAccess` capability.
- Avoid keeping expensive animation loops running when `frame.active === false` (player paused
  or stopped). Use `onPlaybackFrame` to detect the inactive state and pause rendering.
- `onReady` may fire up to 200 ms after the page finishes loading. Design the page to show a
  sensible loading state before `onReady` arrives.
