# Content API

Warnings, the changelog, the community list and verified creators are stored in the backend and served
from `https://spectralis-api.deltavdevs.com`. They used to be files on the legacy CDN. Public reads
need nothing; writes need the admin token and normally happen from telescreen.

Routes are listed in [api-contract.md](api-contract.md). Code is in `backend/src/content.rs`.

---

## Warnings

`GET /spectralis/v1/warnings` returns a list. The app shows the ones that are active and apply to its
version when it starts.

```json
[
  {
    "id": "6.1.0-backend-7-0-0",
    "active": true,
    "title": "Update to Spectralis 7.0.0",
    "message": "The Spectralis backend is now on 7.0.0, so some features in 6.1.0 will stop working.",
    "severity": "critical",
    "versions": ["6.1.0"],
    "dismissible": true,
    "linkLabel": "Download 7.0.0",
    "linkUrl": "https://spectralis.deltavdevs.com"
  }
]
```

| Field | Rules |
|---|---|
| `id` | Required, unique, letters, numbers, `.`, `_`, `-`, up to 64. The app remembers dismissed ids. |
| `severity` | `info`, `warning` (default) or `critical`. |
| `versions` | Optional. Exact app versions the notice is for. Leave out for everyone. |
| `dismissible` | Defaults to `true`. `false` keeps it up and only lets the user follow the link. |
| `linkLabel`, `linkUrl` | Both or neither. The link must be `https`. |
| `title` 120, `message` 1000 | Required. |

At most 50 warnings.

---

## Changelog

`GET /spectralis/v1/changelog` returns releases, newest first. The first one is "Latest" on the website.

```json
[
  {
    "version": "7.0.0",
    "label": "Protocol v2, gapless audio & the timeline editor",
    "date": "Latest",
    "summary": "…",
    "metrics": ["Protocol v2", "Gapless + crossfade"],
    "groups": [
      { "icon": "Users", "title": "Shared Play & Streamer Queue", "bullets": ["…"] }
    ]
  }
]
```

`version` is a display label up to 40 characters, normally a version but sometimes a range such as
`4.0.x - 4.1.x`. `date` is `Latest`, `Previous` or a date. `icon` must be one the website knows
(`CHANGELOG_ICONS` in `frontend/src/data/site.jsx`). Up to 100 releases, 12 groups each, 20 bullets
each.

---

## Community

`GET /spectralis/v1/community` returns `{ name, subtitle, avatar }` entries for the community bar.
`avatar` is an `https` link, normally `…/spectralis/v1/community/avatars/{slug}`.

---

## Verified creators

`GET /spectralis/v1/creators/{fingerprint}` — `{fingerprint}` is the SHA-256 of the creator's Ed25519
public key, lowercase hex (a trailing `.json` is accepted). The player calls it when it meets an
unknown signing key in a `.spectralis` or `.spectral` capsule and re-checks periodically for
revocations.

```json
{
  "keyId": "deltawave-posted-2026-05-15",
  "fingerprint": "6961405f930dad8a49271cfa49936d161e2c8943645be3d355598f0f76c48d60",
  "displayName": "DeltaWave",
  "profileUrl": "https://www.deltavdevs.com",
  "avatarUrl": "https://spectralis-api.deltavdevs.com/spectralis/v1/creators/6961…/avatar",
  "status": "active",
  "allowedCapabilities": ["album.world", "webview.localContent"],
  "createdAtUtc": "2026-05-15T10:30:43Z",
  "updatedAtUtc": "2026-10-04T12:00:00Z",
  "revokedAtUtc": null
}
```

- **`404`**: the key isn't registered. The player rejects the capsule.
- **`status`** is `active`, `suspended` (treated as revoked) or `revoked` (permanent). A revoked or
  suspended key is still served, with `revokedAtUtc` set, so the player can reject its capsules even
  if it trusted them before.
- **Caching**: `Cache-Control: public, max-age=300`. Five minutes keeps revocations meaningful
  within a session without hammering the service on every capsule.
- The player caches key metadata at `%LocalAppData%\Spectralis\trusted-creators.json`.

### Allowed capabilities

The list is the most a creator's capsules may ask for. The player intersects it with what a capsule
requests, and a capsule asking for anything not listed is rejected.

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
| `presence.richPresence` | Capsule's embedded HTML may override Discord rich presence text while playing |
| `worlds.wasm3d` | Capsule may include a sandboxed Wasm/wgpu 3D album world |
| `audio.dspPreset` | Capsule's embedded HTML/Wasm content may register a whole-rack DSP preset while active |
| `worlds.pointerLock` | Capsule's embedded HTML/Wasm content may request OS-level pointer lock |
| `worlds.pauseMenu` | Capsule may customize the pause menu's title and copy |

The backend accepts any dotted capability name so a new one never needs a backend change; unknown names
grant nothing, because the player only honours what it recognises.

---

## Writing content

Everything below is `PUT` with `Authorization: Bearer <SPECTRALIS_ADMIN_TOKEN>` under
`/spectralis/v1/admin`. A request that fails validation is refused with a plain message and changes
nothing; the published list stays as it was.

- `PUT /warnings`, `/changelog`, `/community`: the whole list, replaced in one go.
- `PUT /community/avatars/{slug}`: image bytes. `slug` is lowercase letters, numbers and dashes.
- `PUT /creators/{fingerprint}`: `displayName`, optional `profileUrl`, `keyId`, `status`,
  `allowedCapabilities`. The server sets `updatedAtUtc`, keeps `createdAtUtc`, and stamps
  `revokedAtUtc` the first time a key stops being active (clearing it if the key is made active
  again). A first registration may carry `createdAtUtc` and `revokedAtUtc`, so an import keeps a key's
  real history; after that an update can't rewrite them.
- `PUT /creators/{fingerprint}/avatar`: image bytes. The creator must be registered first.
- `DELETE /creators/{fingerprint}`: removes the key and its avatar. Revoke instead if you want a record.
- `GET /creators`: every creator, for admin tools.

Avatars are PNG, JPEG or WebP, checked from the bytes (not the `Content-Type`), up to 2 MB.

### Telescreen

`telescreen.deltavdevs.com` has a Spectralis section for all of this: warnings, changelog and
community are editable by admins; creators and their permissions, including revoking a key, are
owner-only because they decide what a capsule may do on someone's machine. It sends the signed-in
person's email as `X-Admin-Actor`.

Set the same value for `SPECTRALIS_ADMIN_TOKEN` on the backend and on telescreen (24 or more
characters, for example `openssl rand -base64 36`). To rotate it, change both and redeploy both. The
backend answers `503` on the admin routes while it is unset.

### Loading the old files

`tools/content-import/import.mjs` loads the legacy `warning.json`, `changelog.json`, `community.json`
(with avatars) and `keys/` (with avatars) into the backend. It is safe to run again.

```powershell
$env:SPECTRALIS_ADMIN_TOKEN = "<token>"
node tools/content-import/import.mjs --source Y:/spectralis --api https://spectralis-api.deltavdevs.com
node tools/content-import/import.mjs --source Y:/spectralis --api https://spectralis-api.deltavdevs.com --apply
```

Without `--apply` it only validates what it would send.
