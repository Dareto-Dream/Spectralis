# Spectralis Capsule Format (`.spectralis`)

Capsule files are a signed binary format for distributing self-contained single-track audio
experiences. They are distinct from MP3-embedded metadata and are not standard audio files.

For the multi-track album format, see [spectral-album-world.md](spectral-album-world.md).

---

## Binary Layout

```
[4]   Magic bytes: 0x53 0x50 0x43 0x43  ('SPCC')
[4]   Format version: 3  (Int32 little-endian)
[32]  Ed25519 public key  (creator signing key)
[64]  Ed25519 signature over all bytes that follow
[n]   Payload: a standard ZIP archive
```

- **Fingerprint** = `SHA256(public_key_bytes)` as a lowercase hex string.
- **Signature** covers the raw ZIP payload bytes only (everything after the 104-byte header).
- Header total: 104 bytes (4 + 4 + 32 + 64).

---

## ZIP Contents

```
manifest.json         required   CapsuleManifest schema
audio/<entry>         required   audio file; path matches manifest.audio.entry
reactive.json         optional   ReactiveTimelineDocument for track-reactive metadata
assets/images/*       optional   cover art; first entry used as album art
assets/data/*.lrc     optional   LRC lyrics; first .lrc entry loaded
```

### Manifest Schema (`spectralis-capsule` v3)

```json
{
  "format": "spectralis-capsule",
  "formatVersion": 3,
  "id": "my-track-id",
  "title": "Track Title",
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
  "capabilities": ["webview.localContent"],
  "audio": {
    "entry": "audio/track.flac",
    "sha256": "lowercase-hex-sha256-of-audio-bytes",
    "durationSeconds": 210.5
  },
  "assets": {
    "images": ["assets/images/cover.png"],
    "data": ["assets/data/lyrics.lrc"]
  },
  "story": { },
  "suppressAppLyrics": false
}
```

### `suppressAppLyrics`

Set to `true` when the capsule's embedded visualizer renders the lyrics itself and the normal
Spectralis side lyrics panel would compete for space. The LRC can still be included in
`assets/data` and embedded module data refs.

---

## Story Explainer

Capsules can opt into a click-through story explainer by adding a `story` object to
`manifest.json`. `CapsuleStoryRenderer` picks a presentation in this priority order:

1. **Custom HTML** — `story.entry`, if set and present in the ZIP.
2. **Synthesized pager** — built from `story.pages` or `story.chapters` if either is non-empty.
3. **Backstory pager** — a single synthesized page from `story.backstory`, if set.
4. Nothing — no story surface is shown.

### Custom story page

For full creative control, ship your own HTML/CSS/JS story page instead of the built-in pager —
the same concept as a `.spectral` album world's `world.entry`, just for a single-track capsule.

```json
"story": {
  "entry": "story/index.html",
  "binaryAssets": { "bg": "story/assets/bg.webp" },
  "dataAssets": { "config": "story/assets/config.json" }
}
```

| Field | Required | Description |
|---|---|---|
| `entry` | Yes (for custom mode) | Path within ZIP to the HTML file that boots the story |
| `binaryAssets` | No | Named binary assets (images, fonts) available at the virtual host |
| `dataAssets` | No | Named text/JSON assets available at the virtual host |

The custom story page is hosted the same way any embedded HTML capsule content is: it gets the
full `window.spectral` bridge (see the bootstrap script built by
`WebViewHostService.BuildBootstrapScript` in `Spectralis.Core/Integrations/Web/WebViewHostService.cs`) —
`spectral.meta` for track metadata, `spectral.resume()`/`spectral.pause()`/`spectral.seek(sec)` to
control playback, and `spectral.exit()` to leave the story and return to normal playback. No new
message types were added — a single-track capsule only ever has one track to "start," so
`spectral.resume()` covers it.

If `entry` is absent, or the file it names isn't in the ZIP, the player falls back to the built-in
synthesized pager below — same precedent as album worlds falling back to the plain tracklist when
no `world.entry` is declared.

### Synthesized pager (no-code fallback)

Story pages live in `story.pages` or `story.chapters`. A default explainer image is resolved
from `story.image`, `story.imageEntry`, `story.explainerImage`, or `story.characterImage`. If
none of these are set, the player falls back to `assets/images/character.png`.

Each page supports: `title`, `speaker`, `text`, and image overrides (`image`, `imageEntry`,
`explainerImage`, `characterImage`, `portrait`, `sprite`). Only PNG entries are displayed.
The image path must point to a file inside the ZIP.

---

## Opening Flow

1. `CapsuleReader.Read(path)` — validates SPCC magic, version 3, verifies Ed25519 signature
   (BouncyCastle), computes fingerprint, reads `manifest.json`.
2. `CapsuleCdnClient.FetchCreatorKeyAsync(fingerprint)` — `GET /spectralis/keys/{fingerprint}.json`.
   Falls back to `CreatorTrustStore` cache on network failure.
3. Reject if key is 404, `status` is not `active`, or `revokedAtUtc` is set.
4. Intersect `manifest.capabilities` with `keyMetadata.allowedCapabilities`; reject if any
   requested capability is missing from the CDN key.
5. Check `CreatorTrustStore.IsTrusted(fingerprint)`; if not trusted, show `CreatorTrustDialog`.
6. Cache updated metadata; call `trustStore.Trust(fingerprint, displayName)` on first approval.
7. Extract audio to a temp file (`%TEMP%\spectralis-capsule-{guid}{ext}`), verify SHA-256
   against `manifest.audio.sha256`.
8. Load via `engine.Load(tempPath, trackInfo)` with metadata from the manifest.
9. Load `reactive.json` if present via `LoadReactiveDocument`.

---

## Rules

- Capsule audio temp files are deleted when the capsule is unloaded or the form closes.
- `UnloadCapsule()` is called before any new local file load; local audio and capsule audio never
  coexist in the engine.
- Capsule files are not added to the play queue; they replace the current track.
- The `CreatorTrustStore` persists at `%LocalAppData%\Spectralis\trusted-creators.json`.

---

## Capability Constants

See [../cdn-contract.md](../cdn-contract.md) for the full capability list and CDN key structure.

| Capability | Common usage |
|---|---|
| `webview.localContent` | Embedded HTML visualizer served from extracted capsule assets |
| `visualizer.wasm` | Embedded WASM visualizer module |
| `visualizer.multiLayer` | Composable multi-layer visualizer |
| `visualizer.shaderPack` | Shader pack bundle |
| `sharedPlay.hostCapsule` | Capsule can be hosted via Shared Play |
| `sharedPlay.packageUpload` | Capsule assets may be uploaded for Shared Play |
| `timeline.appControl` | Reactive timeline may issue app control events |
