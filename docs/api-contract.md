# Spectralis Service Contract

Where the app, website, Discord bot and chat bridge talk to Spectralis's own services, and what each
route does. Everything is self-hosted; nothing is read as a file off a third-party CDN or an
auto-generated Railway address.

Other docs: [content-api.md](content-api.md) (warnings, changelog, community, verified creators),
[rooms.md](rooms.md) (temporary and permanent Shared Play rooms), [cdn-contract.md](cdn-contract.md)
(releases and installers), [realtime-protocol.md](realtime-protocol.md) (websockets) and
[streamer-queue-webhooks.md](streamer-queue-webhooks.md).

---

## Origins

| Origin | What lives there |
|---|---|
| `https://spectralis-api.deltavdevs.com` | The backend (Rust, Railway). Content, accounts, rooms, Shared Play, Streamer Queue, web-share pages. |
| `https://spectralis-cdn.deltavdevs.com` | Release downloads and update feeds (Cloudflare R2). See [cdn-contract.md](cdn-contract.md). |
| `https://player.deltavdevs.com` | The Player site: room finder, rooms, sign-in approval. Its own repo (`Dareto-Dream/player`). |
| `https://spectralis.deltavdevs.com` | The website: downloads, changelog, docs. |

The app's one constant for the API is `SpectralisEndpoints.ApiBase` in `Spectralis.Core/Platform`.
The website has `API_BASE` and `DOWNLOAD_BASE` in `frontend/src/data/site.jsx`. Nothing else should
spell these addresses out.

### Legacy origins

`cdn.deltavdevs.com/spectralis` and the old `audioplayer-production-…up.railway.app` address are
still reachable, because installed apps from 7.0.0 and earlier have them built in and read their
warnings, changelog and update feed from there. New code never uses them. Keep the legacy CDN
published (`deploy.ps1` does this) until those installs have moved on.

Production deploys from the `main` branch. Telescreen (the admin console) edits content through the
admin routes below.

---

## Rules

- JSON that depends on who is asking, and everything under `/player/v1`, is `Cache-Control: no-store`.
- Public content is cacheable: warnings, changelog and community for 60 seconds, creator keys for 5
  minutes so a revocation lands within a session.
- Errors are `{ "error": "message", "status": 400 }`. The message is written for a person.
- A route that can change data and isn't public needs an account (`Authorization: Bearer <Ward or
  app token>`), a session key (`x-session-key`) or the admin token. The admin routes answer `503`
  until `SPECTRALIS_ADMIN_TOKEN` is set.
- Large files never go through the backend except Shared Play packages, which have their own limit.

---

## Content (public reads)

| Route | Returns |
|---|---|
| `GET /spectralis/v1/warnings` | Notices the app shows on start. |
| `GET /spectralis/v1/changelog` | Releases for the website changelog, newest first. |
| `GET /spectralis/v1/community` | People shown in the website's community bar. |
| `GET /spectralis/v1/community/avatars/{slug}` | An avatar image. |
| `GET /spectralis/v1/creators/{fingerprint}` | A verified creator and what their capsules may do, or `404` if the key isn't registered. |
| `GET /spectralis/v1/creators/{fingerprint}/avatar` | The creator's avatar image. |

Shapes, validation and the admin routes are in [content-api.md](content-api.md).

---

## Accounts and rooms (Player API)

Sign-in is Ward. The app connects through Player's Ward client, so no OAuth secret ships in the
desktop binary.

| Route | Purpose |
|---|---|
| `POST /player/v1/connect` | Start connecting the app to a Ward account; returns a code and a verification URL on the Player site. |
| `POST /player/v1/connect/{code}` | Poll with the secret until the account is approved; returns the app token. |
| `POST /player/v1/connect/{code}/approve` | Called from the Player site by the signed-in user. |
| `GET /player/v1/me`, `DELETE /player/v1/me` | Who the token belongs to; disconnect this app. |
| `GET /player/v1/me/room` | The account's one permanent room, or `{ "room": null }`. |
| `GET /player/v1/me/rooms` | Everything the account owns, including Streamer Queue rooms. |
| `GET /player/v1/rooms` | Public permanent rooms (the room finder). |
| `POST /player/v1/rooms` | Make the account's permanent room. `409` if it already has one. |
| `GET /player/v1/rooms/{id}` | A room card. |
| `PUT /player/v1/rooms/{id}/profile` | Name, description, tags, access, public or private. Host only. |
| `PUT/GET /player/v1/rooms/{id}/images/{banner,icon,social}` | Room artwork (PNG, JPEG or WebP, 5 MB). |
| `GET /player/v1/rooms/{id}/access`, `POST` | Whether you may join; ask to join an approval room. |
| `GET /player/v1/rooms/{id}/members`, `PUT …/members/{subject}` | Host manages admission. |

The room model is explained in [rooms.md](rooms.md).

---

## Shared Play

Dynamic state is under `/shared-play/v2`. A session is one live listening party.

| Route | Purpose |
|---|---|
| `POST /shared-play/v2/sessions` | Start a session. Signed in, it belongs to the account; signed out, it is temporary. The response says which (`temporary`). |
| `GET /shared-play/v2/sessions/{code}` | The session manifest. |
| `GET …/state`, `GET …/queue`, `POST …/queue/items` | Playback state, the shared queue, listener requests. |
| `…/presence`, `…/reactions` | Listener heartbeat and short-lived reactions. |
| `PUT/GET …/package` | The rich package (zip) holding the audio and track data. |
| `GET …/socket` | The realtime websocket ([realtime-protocol.md](realtime-protocol.md)). |
| `POST …/end` | End the party. |
| `GET/PUT /shared-play/v2/channels/{id}` | The permanent room's live pointer: which session is live now, kept fresh by the host's heartbeat. |

Join links look like `https://player.deltavdevs.com/sessions/{code}`. The browser listener page is
served by the backend at `/spectralis/web-share/`.

---

## Streamer Queue

Routes under `/streamer-queue/v1` (rooms, settings, submissions, uploads) and `/streamer-queue/v2`
(realtime socket and the chat-bot-agnostic webhook API). See
[streamer-queue-webhooks.md](streamer-queue-webhooks.md) and
[realtime-protocol.md](realtime-protocol.md).

---

## Admin routes

All under `/spectralis/v1/admin`, all `Authorization: Bearer <SPECTRALIS_ADMIN_TOKEN>`.

| Route | Purpose |
|---|---|
| `PUT /warnings`, `/changelog`, `/community` | Replace a whole list. |
| `PUT /community/avatars/{slug}` | Upload an avatar. |
| `GET /creators`, `PUT/DELETE /creators/{fingerprint}`, `PUT /creators/{fingerprint}/avatar` | Verified creators. |
| `POST /rooms/wipe` | One-off: delete every permanent room and hand back a backup. See [rooms.md](rooms.md). |

Telescreen is the normal way to use these.

---

## Health

`GET /health` answers `{ "ok": true, "protocolVersion": "shared-play-v2", "service": "spectralis-backend" }`.
