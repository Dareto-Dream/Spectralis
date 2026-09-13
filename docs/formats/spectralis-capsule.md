# Spectralis Capsule Format (`.spectralis`)

Capsule files are a signed binary format for distributing self-contained single-track audio
experiences — not to be confused with the MP3-embedded metadata modules, this is a whole
different format and not a standaone audio file you can just open in anything.

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
Spectralis side lyrics panel would just compete for space — two lyric displays fighting each
other is nobody's idea of a good time. The LRC can still be included in `assets/data` and
embedded module data refs.

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
same idea as a `.spectral` album world's `world.entry`, just scaled down for a single track.

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

- Capsule audio temp files get deleted when the capsule is unloaded or the form closes, no
  leftover junk in `%TEMP%`.
- `UnloadCapsule()` is called before any new local file load; local audio and capsule audio never
  coexist in the engine.
- Capsule files are not added to the play queue — they replace the current track instead.
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
| `presence.richPresence` | Embedded HTML may override Discord rich presence text (see below) |
| `worlds.wasm3d` | Capsule may include a sandboxed Wasm/wgpu 3D album world |
| `audio.dspPreset` | Embedded HTML/Wasm content may register a whole-rack DSP preset (see below) |

---

## DSP Preset Control (`audio.dspPreset`)

A capsule that declares `audio.dspPreset` can register a whole-rack effects preset that is
auto-applied while its surface is on screen, built only from the app's own effect building
blocks — a preset can never inject arbitrary DSP, only reference effect names the app already
knows how to construct.

```js
// Register — preset is the same shape the app's own saved chain presets use.
window.spectral.dsp.register({
  Enabled: true,
  Effects: [
    { Name: "Parametric EQ", Enabled: true, Params: { "preamp": 0, "bandCount": 3, /* ... */ } },
    { Name: "Stereo Widener", Enabled: true, Params: { "amount": 0.4 } }
  ]
});

// Release — reverts to the user's own chain.
window.spectral.dsp.release();
```

Rules:

- The user's own rack is snapshotted the moment a preset is first applied and restored verbatim
  on release — never persisted to the user's saved settings while a world preset is active.
- Only one world preset is active at a time; registering again while active replaces it in place
  without re-snapshotting (so the user's original chain, not the previous world preset, is what
  comes back on release).
- Auto-released when the capsule surface unloads, a session reset happens, or the capsule did not
  declare the capability (drops the message silently — calling `spectral.dsp.register()`
  unconditionally is safe).
- Applying a preset rebuilds the audio output device (~800ms), same cost as loading any other
  whole-rack preset — avoid registering/releasing rapidly.

---

## Discord Rich Presence (`presence.richPresence`)

A capsule that declares `presence.richPresence` can drive the user's Discord status from its
embedded HTML while the capsule audio is playing — handy for story capsules that want the
status line to follow the narrative ("Chapter 3 — the descent") instead of the plain track title.

```js
// Set — all fields optional strings, clamped to 128 chars host-side.
window.spectral.presence.set({
  details: "The Line — Chapter 3",
  state: "descending the shaft",
  largeImageText: "Spectralis capsule",
  smallImageText: "paused"
});

// Revert to the normal track presence.
window.spectral.presence.clear();
```

Rules:

- Only honoured while this capsule's surface is on screen. Leaving the capsule, a session reset,
  or the visualizer being torn down clears the override automatically.
- Timing (elapsed / remaining) still comes from the engine — the capsule controls text only,
  not the progress bar.
- Messages are dropped silently if the capsule did not declare the capability, so calling
  `spectral.presence.set()` unconditionally is safe.
- Honoured regardless of whether the user has any Spotify presence going; the capsule wins
  while it is active.
