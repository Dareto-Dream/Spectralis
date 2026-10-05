# Spectralis Backend

This is the Rust backend behind Spectralis. Production is hosted at:

```text
https://spectralis-api.deltavdevs.com
```

It serves, from one binary:

- **Shared Play** under `/shared-play/v2`: sessions, state, queue, presence, reactions, packages, the
  realtime socket and permanent-room pointers. Rooms are either temporary (signed out, link-only) or
  permanent (one per Ward account); see [`docs/rooms.md`](../docs/rooms.md).
- **Accounts and rooms** under `/player/v1`: Ward sign-in for the app, the room finder, room profiles,
  artwork and admission.
- **Streamer Queue** under `/streamer-queue/v1` and `/v2`, including the realtime socket and the
  chat-bot-agnostic webhook API.
- **Content** under `/spectralis/v1`: warnings, changelog, community and verified creators, read in
  public and written with the admin token; see [`docs/content-api.md`](../docs/content-api.md).
- The browser listener page at `/spectralis/web-share/`.

[`docs/api-contract.md`](../docs/api-contract.md) lists every route. The code is split by area:
`collab.rs` (rooms and sockets), `player.rs` (accounts and admission), `rooms.rs` (the room model),
`content.rs`, `sq_realtime.rs`, `sq_webhooks.rs`, `protocol.rs` and `store.rs`.

## Run Locally

```powershell
cargo run --manifest-path .\backend\Cargo.toml
```

The server listens on `http://0.0.0.0:8787` by default and stores session data under `backend/data/`.

Useful environment variables:

| Variable | Default | Purpose |
|---|---|---|
| `PORT` | `8787` | Port to bind. Railway provides this automatically. |
| `SPECTRALIS_BACKEND_HOST` | `0.0.0.0` | Host/interface to bind. |
| `SPECTRALIS_BACKEND_DATA` | `backend/data` | Session/package storage path. Use a Railway volume for production. |
| `SPECTRALIS_WEB_SHARE_ROOT` | `web-share` | Path to the browser Shared Play player assets. |
| `SPECTRALIS_PUBLIC_BASE_URL` | inferred from forwarded headers | Public origin to put into generated URLs. |
| `SPECTRALIS_ADMIN_TOKEN` | unset | Bearer token for the `/spectralis/v1/admin` routes (warnings, changelog, community, creators, the room wipe). 24 or more characters. While it is unset those routes answer `503`. Telescreen sends the same value. |
| `WARD_ISSUER` | `https://ward.deltavdevs.com` | The Ward server accounts sign in through. |

## Use With Spectralis

The desktop app requires an HTTPS Shared Play base URL. For local testing, put the backend behind a tunnel:

```powershell
cloudflared tunnel --url http://127.0.0.1:8787
```

Then set **Settings -> Shared Play CDN Base URL** to the generated HTTPS origin, for example:

```text
https://example.trycloudflare.com
```

If your proxy does not send `X-Forwarded-Proto` and `X-Forwarded-Host`, pass the public origin explicitly:

```powershell
$env:SPECTRALIS_PUBLIC_BASE_URL="https://example.trycloudflare.com"
cargo run --manifest-path .\backend\Cargo.toml
```

## Railway

The repo includes [`railway.toml`](railway.toml) and [`Dockerfile`](Dockerfile). Railway should build this service from the backend Dockerfile and run `spectralis-backend`.

Railway-specific notes:

- Bind to `0.0.0.0:$PORT`; the Rust server does this automatically.
- Add a Railway volume mounted at `/data` if you want Shared Play packages to survive restarts.
- Set `SPECTRALIS_BACKEND_DATA=/data` for the volume-backed store. The Dockerfile already sets this.
- Usually you can leave `SPECTRALIS_PUBLIC_BASE_URL` unset because Railway forwards host/proto headers.

## Discord Activity URL Mappings

The app's Discord activity links point at the Player site:

```text
https://player.deltavdevs.com/sessions/{code}?source=discord
```

so the Activity's URL mapping in the Discord developer portal has to map `player.deltavdevs.com`, not the
backend:

```text
PREFIX: /
TARGET: player.deltavdevs.com
```
