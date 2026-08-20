# Spectralis CDN Contract

This document defines the endpoints and response shapes that Spectralis expects from its public
services. It is a contract for the hosted services, not for the player.

See [api-contract.md](api-contract.md) for the planned CDN/API split and route ownership rules.

---

## Base URLs

```
CDN:     https://cdn.deltavdevs.com
Backend: https://audioplayer-production-5b83.up.railway.app
```

Squirrel updates and creator keys use the CDN origin. Shared Play session creation and state use
the backend origin.

---

## Squirrel Update Feed

```
GET https://cdn.deltavdevs.com/spectralis
```

Squirrel reads this path for the update manifest. Handled by the Squirrel-compatible release
pipeline. Do not move this route.

---

## Installed Visualizer Manifest

```
GET https://cdn.deltavdevs.com/spectralis/visualizers
```

Returns a JSON manifest listing visualizers available for redemption codes.

**Response** (array):

```json
[
  {
    "id": "visualizer-id",
    "displayName": "Visualizer Name",
    "redemptionCode": "CODE",
    "wasmUrl": "https://...",
    "htmlUrl": null
  }
]
```

---

## Creator Key Metadata

```
GET https://cdn.deltavdevs.com/spectralis/keys/{fingerprint}.json
```

- `{fingerprint}` — SHA-256 of the creator's Ed25519 public key, lowercase hex.

The player fetches this when it encounters an unknown signing key in a `.spectralis` or `.spectral`
capsule. It is also re-fetched periodically to check for revocations.

**Response — 200 OK**:

```json
{
  "keyId": "creator-key-id",
  "fingerprint": "sha256-hex-of-public-key",
  "displayName": "Creator Display Name",
  "profileUrl": "https://example.com/creator",
  "avatarUrl": "https://cdn.deltavdevs.com/spectralis/keys/avatars/fingerprint.jpg",
  "status": "active",
  "allowedCapabilities": [
    "visualizer.multiLayer",
    "visualizer.shaderPack",
    "webview.localContent",
    "album.world"
  ],
  "createdAtUtc": "2026-01-01T00:00:00Z",
  "updatedAtUtc": "2026-01-01T00:00:00Z",
  "revokedAtUtc": null
}
```

**Response — 404**: Key not registered on CDN. Player rejects capsule.

**Response — 200 with `"status": "revoked"`** or `"revokedAtUtc"` set: Player rejects capsule even
if previously trusted.

### Key Status Values

| `status` | Meaning |
|---|---|
| `active` | Key is valid. Playback allowed after user trust prompt. |
| `suspended` | Temporarily disabled. Treat as revoked. |
| `revoked` | Permanently disabled. Always reject. |

### Allowed Capabilities

The CDN metadata lists the maximum capabilities a creator is allowed to request. The player
intersects the capsule's requested capabilities with this list. Any requested capability not in
`allowedCapabilities` causes the capsule to be rejected.

| Capability | Effect |
|---|---|
| `app.theme.deepControl` | Capsule can set deep shell theme overrides |
| `app.layout.deepControl` | Capsule can modify shell layout |
| `app.chrome.effects` | Capsule can apply window chrome effects |
| `visualizer.multiLayer` | Capsule can compose multiple visualizer layers |
| `visualizer.wasm` | Capsule can embed a WASM visualizer module |
| `visualizer.shaderPack` | Capsule can supply shader packs |
| `webview.localContent` | Capsule can load local HTML content in WebView2 |
| `webview.networkAccess` | Capsule's WebView content may access the network |
| `album.world` | Capsule may include an interactive album world |
| `sharedPlay.hostCapsule` | Capsule can be hosted via Shared Play |
| `sharedPlay.packageUpload` | Capsule assets may be uploaded for Shared Play |
| `timeline.appControl` | Capsule's reactive timeline may issue app control events |

### Caching Policy

Player caches key metadata at `%LocalAppData%\Spectralis\trusted-creators.json`.

The CDN should set:

```
Cache-Control: public, max-age=300
```

Five minutes is short enough to make revocations meaningful within a session.

---

## Shared Play

All Shared Play dynamic routes live under the backend origin:

```
https://audioplayer-production-5b83.up.railway.app
```

### Create Upload Session

```
POST /shared-play/v1/upload-urls
```

**Response**:

```json
{
  "sessionId": "session-id",
  "joinUrl": "https://cdn.deltavdevs.com/spectralis/web-share/index.html?session=session-id",
  "stateUrl": "https://..../shared-play/v1/sessions/session-id/state",
  "queueUrl": "https://..../shared-play/v1/sessions/session-id/queue",
  "presenceUrl": "https://..../shared-play/v1/sessions/session-id/presence",
  "reactionsUrl": "https://..../shared-play/v1/sessions/session-id/reactions",
  "expiresAtUtc": "2026-05-18T00:00:00Z",
  "uploads": [
    {
      "name": "spectralis-package",
      "method": "PUT",
      "uploadUrl": "https://..../shared-play/v1/uploads/session-id/spectralis-package",
      "assetUrl": "https://..../shared-play/v1/packages/session-id/spectralis-rich.zip",
      "headers": {
        "content-type": "application/vnd.spectralis.shared-play+zip"
      }
    }
  ]
}
```

See `SharedPlay/SharedPlayCdnClient.cs` for full request/response shapes.

### Web Share Player

```
GET https://cdn.deltavdevs.com/spectralis/web-share/index.html?session={sessionId}
GET https://cdn.deltavdevs.com/spectralis/web-share/index.html?session={sessionId}&source=discord&mode=activity
```

The Discord-flavored URL is the same browser player with an activity-oriented layout and Open
Graph metadata for chat previews. It can also serve as the Discord Activity URL target.

---

## All Paths Summary

| Path | Origin | Purpose |
|---|---|---|
| `/spectralis` | CDN | Squirrel update feed |
| `/spectralis/visualizers` | CDN | Redeemable visualizer manifest |
| `/spectralis/web-share/index.html` | CDN | Shared Play browser player |
| `/spectralis/keys/{fingerprint}.json` | CDN | Creator signing key metadata |
| `/shared-play/v1/upload-urls` | Backend | Shared Play session creation |
| `/shared-play/v1/uploads/{sessionId}/spectralis-package` | Backend | Rich package upload |
| `/shared-play/v1/sessions/{sessionId}` | Backend | Session manifest |
| `/shared-play/v1/sessions/{sessionId}/state` | Backend | Playback state |
| `/shared-play/v1/sessions/{sessionId}/queue` | Backend | Shared queue snapshot |
| `/shared-play/v1/sessions/{sessionId}/queue/items` | Backend | Guest queue requests |
| `/shared-play/v1/sessions/{sessionId}/presence` | Backend | Room listener heartbeat |
| `/shared-play/v1/sessions/{sessionId}/reactions` | Backend | Short-lived room reactions |
| `/shared-play/v1/sessions/{sessionId}/tracks/{trackKey}/activate` | Backend | Activate pre-uploaded track |
| `/shared-play/v1/channels/{channelId}` | Backend | Permanent Live Channel pointer |
| `/shared-play/v1/channels/{channelId}/stats` | Backend | Live Channel aggregate stats |
| `/shared-play/v1/packages/{sessionId}/spectralis-rich.zip` | CDN/Backend | Rich package download |
