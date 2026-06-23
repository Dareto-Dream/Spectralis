# Spectralis App Service Contract

This document defines the service routes Spectralis expects once the combined Railway system is
split by responsibility:

- **CDN service** — serves public/static and WebDAV-compatible content on port `8080`.
- **Backend service** — serves dynamic API, uploads, licenses, sessions, and trust mutation on port `3000`.
- **Update route** — stays exactly where it is today.

This is the application routing contract. It describes the target refactor but does not require
the desktop app to implement every route immediately.

See [cdn-contract.md](cdn-contract.md) for the current endpoint shapes the app actually calls.

---

## Origins

### Production

```txt
CDN origin:  https://cdn.deltavdevs.com
API origin:  https://api.deltavdevs.com
Update feed: https://cdn.deltavdevs.com/spectralis
```

The CDN and API may run in one Railway project or repository, but they are separate route
surfaces. A reverse proxy, Railway domain mapping, or gateway should route public HTTPS traffic
to the correct internal port.

```txt
cdn.deltavdevs.com -> CDN service port 8080
api.deltavdevs.com -> backend service port 3000
```

### Local Development

```txt
CDN local: http://127.0.0.1:8080
API local: http://127.0.0.1:3000
```

Production origins must be HTTPS. Local development may use plain HTTP on loopback.

---

## App Configuration

| Setting | Default | Purpose |
|---|---|---|
| `SpectralisCdnBaseUrl` | `https://cdn.deltavdevs.com` | Public static files, package downloads, web player, manifests, creator key reads |
| `SpectralisApiBaseUrl` | `https://api.deltavdevs.com` | Dynamic routes, upload sessions, Shared Play state, license and redemption APIs |
| `SpectralisUpdateFeedUrl` | `https://cdn.deltavdevs.com/spectralis` | Squirrel update feed. Do not move. |

Rules:

- Do not infer the API origin from the CDN origin once both are configured.
- Do not send large uploads through the backend unless the route is explicitly designed for streaming.
- Do not download large packages through the backend. Return CDN asset URLs instead.
- Dynamic JSON responses should be `Cache-Control: no-store` unless this document says otherwise.
- Static immutable assets should use long cache lifetimes.
- Creator key metadata should use short public caching so revocations propagate quickly.

---

## CDN Service Routes

### Squirrel Updates

```http
GET /spectralis
GET /spectralis/RELEASES
GET /spectralis/Setup.exe
GET /spectralis/{package-file}.nupkg
```

Do not move app update checks to the API.

### Public Web Share Player

```http
GET /spectralis/web-share/index.html
GET /spectralis/web-share/player.js
GET /spectralis/web-share/styles.css
GET /spectralis/web-share/index.html?session={sessionId}
GET /spectralis/web-share/index.html?session={sessionId}&source=discord&mode=activity
```

Static files are served by the CDN. The web player reads session/state data from the API.
User-facing Shared Play links should point at this CDN route, not directly at a JSON API route.

### Redeemable Visualizer Manifest

```http
GET /spectralis/visualizers
GET /spectralis/visualizers.json
GET /spectralis/visualizers/manifest.json
GET /spectralis/visualizers/index.json
```

The app tries multiple candidates. The CDN should make at least the canonical path work.

```json
[
  {
    "id": "visualizer-id",
    "displayName": "Visualizer Name",
    "redemptionCode": "CODE",
    "packageUrl": "https://cdn.deltavdevs.com/spectralis/visualizers/visualizer-id/package.json",
    "wasmUrl": "https://cdn.deltavdevs.com/spectralis/visualizers/visualizer-id/module.wasm",
    "htmlUrl": null
  }
]
```

### Creator Key Metadata

```http
GET /spectralis/keys/{fingerprint}.json
GET /spectralis/keys/avatars/{fingerprint}.{ext}
```

- `404` means unknown creator key.
- `status: revoked`, `status: suspended`, or non-null `revokedAtUtc` means reject.
- Recommended: `Cache-Control: public, max-age=300`.
- Writes/revocations go through the API or admin tooling, then publish to the CDN.

### Shared Play Package Downloads

```http
GET /shared-play/v1/packages/{sessionId}/spectralis-rich.zip
GET /shared-play/v1/packages/{sessionId}/browser-audio.{ext}
GET /shared-play/v1/packages/{sessionId}/manifest.json
```

Package downloads should be served by the CDN, not the backend. Use short TTLs or
signed/unguessable paths. Expired sessions should return `404` or `410`.

### WebDAV (Admin)

```http
OPTIONS /webdav/*
PROPFIND /webdav/*
MKCOL /webdav/*
GET /webdav/*    PUT /webdav/*
DELETE /webdav/*
MOVE /webdav/*
COPY /webdav/*
```

WebDAV must be authenticated. Credentials must never ship in the desktop app or browser player.

---

## Backend API Service Routes

### Health

```http
GET /healthz
GET /readyz
```

```json
{ "ok": true }
```

### Shared Play Upload Session

```http
POST /shared-play/v1/upload-urls
Content-Type: application/json
```

Request:

```json
{
  "protocolVersion": "shared-play-v1",
  "clientName": "Spectralis",
  "packageKind": "spectralis-rich",
  "track": {},
  "package": {
    "trackId": "sha256:...",
    "audioSha256": "...",
    "packageSha256": "...",
    "audioBytes": 123,
    "packageBytes": 456,
    "audioExtension": ".mp3",
    "contentType": "application/vnd.spectralis.shared-play+zip"
  },
  "playback": {},
  "capabilities": {
    "spectralisRichPackage": true,
    "preservesEmbeddedMetadata": true,
    "preservesAlbumArt": true,
    "preservesEmbeddedVisualizer": true,
    "browserFallbackIncluded": false
  }
}
```

Response:

```json
{
  "sessionId": "session-id",
  "joinUrl": "https://cdn.deltavdevs.com/spectralis/web-share/index.html?session=session-id",
  "expiresAtUtc": "2026-05-12T00:00:00Z",
  "stateUrl": "https://api.deltavdevs.com/shared-play/v1/sessions/session-id/state",
  "uploads": [
    {
      "name": "spectralis-package",
      "method": "PUT",
      "uploadUrl": "https://api.deltavdevs.com/shared-play/v1/uploads/upload-token",
      "assetUrl": "https://cdn.deltavdevs.com/shared-play/v1/packages/session-id/spectralis-rich.zip",
      "headers": {
        "content-type": "application/vnd.spectralis.shared-play+zip"
      }
    }
  ]
}
```

Maximum rich package size: `536870912` bytes.

### Shared Play Upload Target

```http
PUT /shared-play/v1/uploads/{uploadToken}
```

Tokens must be short-lived and single-use. The backend should avoid buffering whole packages in
memory. On successful upload, the CDN `assetUrl` must become readable.

### Shared Play Session Reads

```http
GET /shared-play/v1/sessions/{sessionId}
GET /shared-play/v1/sessions/{sessionId}/manifest
GET /shared-play/v1/join/{sessionId}
GET /shared-play/join/{sessionId}
```

Response:

```json
{
  "session": {
    "sessionId": "session-id",
    "track": {
      "lyrics": [
        { "timeSeconds": 12.3, "text": "Line text" }
      ]
    },
    "packageUrl": "https://cdn.deltavdevs.com/shared-play/v1/packages/session-id/spectralis-rich.zip",
    "browserAudioUrl": null,
    "stateUrl": "https://api.deltavdevs.com/shared-play/v1/sessions/session-id/state",
    "queueUrl": "https://api.deltavdevs.com/shared-play/v1/sessions/session-id/queue",
    "presenceUrl": "https://api.deltavdevs.com/shared-play/v1/sessions/session-id/presence",
    "reactionsUrl": "https://api.deltavdevs.com/shared-play/v1/sessions/session-id/reactions",
    "expiresAtUtc": "2026-05-12T00:00:00Z"
  }
}
```

### Shared Play State

```http
GET /shared-play/v1/sessions/{sessionId}/state
POST /shared-play/v1/sessions/{sessionId}/state
```

POST body:

```json
{
  "protocolVersion": "shared-play-v1",
  "sessionId": "session-id",
  "trackId": "sha256:...",
  "state": {
    "isPlaying": true,
    "positionSeconds": 12.3,
    "durationSeconds": 180,
    "reason": "tick",
    "hostClockUtc": "2026-05-12T00:00:00Z"
  },
  "playback": {}
}
```

State responses are `Cache-Control: no-store`. State writes should be rate-limited.

### Shared Play Queue

```http
GET /shared-play/v1/sessions/{sessionId}/queue
POST /shared-play/v1/sessions/{sessionId}/queue
POST /shared-play/v1/sessions/{sessionId}/queue/items
```

The host posts the canonical queue snapshot. Browser guests can post a single queue item request to
`/queue/items`; desktop hosts poll the queue and decide how to resolve playable URLs. Host queue
items may include `trackId` and `packageUrl` when the host has pre-uploaded the next local track, so
browser rooms can pre-download it before playback switches.

### Shared Play Track Preload

```http
POST /shared-play/v1/sessions/{sessionId}/tracks
PUT /shared-play/v1/uploads/{sessionId}/tracks/{trackKey}/spectralis-package?activate=false
POST /shared-play/v1/sessions/{sessionId}/tracks/{trackKey}/activate
```

Set `activateOnUpload` to `false` when requesting a track upload to stage the package without
changing the active room track. Later, call `/activate` with playback state to make the pre-uploaded
track current without another package upload.

### Shared Play Presence And Reactions

```http
GET /shared-play/v1/sessions/{sessionId}/presence
POST /shared-play/v1/sessions/{sessionId}/presence
GET /shared-play/v1/sessions/{sessionId}/reactions
POST /shared-play/v1/sessions/{sessionId}/reactions
```

Presence is a lightweight heartbeat. Clients post `{ "clientId": "...", "displayName": "Listener" }`;
the response includes `listenerCount` and active `participants`. Reactions are short-lived room
events. Clients post `{ "clientId": "...", "type": "love", "label": "Love" }`; the response returns
recent `items` with `id`, `type`, `label`, `clientId`, and `createdAtUtc`.

### Shared Play Live Channels

```http
GET /shared-play/v1/channels/{channelId}
PUT /shared-play/v1/channels/{channelId}
GET /shared-play/v1/channels/{channelId}/stats
```

A Live Channel is a permanent link that points to the owner's current Shared Play session while they
are live. The owner writes channel state with an `ownerToken`; public reads omit that token. Channel
responses include `isLive`, `sessionId`, `joinUrl`, current track/playback metadata, `listenerCount`,
and aggregate `stats`.

Stats include:

```json
{
  "totalSharedSeconds": 3600,
  "totalListenerSeconds": 7200,
  "peakConcurrentListeners": 4,
  "trackStats": [
    {
      "trackId": "sha256:...",
      "title": "Song",
      "artist": "Artist",
      "playSeconds": 180,
      "listenerSeconds": 540,
      "reactions": 8
    }
  ]
}
```

### License Routes (Reserved)

```http
GET /licenses/v1/products/spectralis
POST /licenses/v1/redeem
POST /licenses/v1/validate
GET /licenses/v1/entitlements/{accountOrDeviceId}
POST /licenses/v1/refresh
```

User-specific licenses are API-only. Do not serve them from the CDN.

### Visualizer Redemption API

```http
POST /visualizers/v1/redeem
GET /visualizers/v1/entitlements/{accountOrDeviceId}
```

The API decides whether a code is valid and returns CDN package URLs for downloads.

### Creator Key Admin

```http
POST /creators/v1/keys
PATCH /creators/v1/keys/{fingerprint}
POST /creators/v1/keys/{fingerprint}/revoke
POST /creators/v1/keys/{fingerprint}/restore
```

Admin routes require strong authentication. Successful mutations must publish updated metadata to
`/spectralis/keys/{fingerprint}.json` within five minutes.

---

## CDN vs API Ownership Table

| Feature | Use CDN | Use API |
|---|---|---|
| Squirrel updates | Yes, `/spectralis` unchanged | No |
| Web Share player | Yes, `/spectralis/web-share/*` | No |
| Visualizer package bytes | Yes | No |
| Static visualizer manifest | Yes | Optional for protected redemption |
| Creator key metadata reads | Yes | No |
| Creator key mutations | No | Yes |
| Shared Play upload session | No | Yes |
| Shared Play package upload | Only if returned as direct upload target | Yes or signed target |
| Shared Play package download | Yes | No |
| Shared Play state | No | Yes |
| Shared Play queue/presence/reactions | No | Yes |
| Shared Play Live Channels/stats | No | Yes |
| Licenses/entitlements | Public product metadata only | Yes |

---

## Cache Policy

| Route | Cache |
|---|---|
| `/spectralis/RELEASES` | `no-cache` or short cache |
| `/spectralis/Setup.exe` and release packages | immutable or versioned long cache |
| `/spectralis/web-share/index.html` | short cache |
| `/spectralis/web-share/player.js`, `styles.css` | short cache unless versioned |
| `/spectralis/visualizers*` | short to medium cache |
| `/spectralis/keys/{fingerprint}.json` | `public, max-age=300` |
| `/shared-play/v1/sessions/*` API routes | `no-store` |
| `/shared-play/v1/packages/*` | short cache or signed URL policy |
| `/licenses/v1/*` | `no-store` except public product metadata |

---

## Compatibility Notes

- Existing clients expect Shared Play API-like routes under the CDN origin. The combined system
  may temporarily proxy `/shared-play/*` from `cdn.deltavdevs.com` to the backend.
- New clients should call `api.deltavdevs.com` for `/shared-play/*`, `/licenses/*`,
  `/visualizers/v1/*`, and `/creators/v1/*`.
- New clients should call `cdn.deltavdevs.com` for `/spectralis/*` and package/media downloads.
- The Squirrel update route remains `https://cdn.deltavdevs.com/spectralis`.
