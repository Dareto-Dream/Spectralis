# Realtime Protocol v2

One websocket protocol for every room kind (Shared Play, Streamer Queue) and every client (desktop
app, Discord bot, web player; the local OBS overlay uses a small version check, see below). The Rust backend owns the version; the reference
implementation is [`backend/src/protocol.rs`](../backend/src/protocol.rs). The old setup was four
clients each guessing what the server meant, and they drifted. This is the fix: say what you speak,
get told what you agreed on, get a clear error if you're too old.

## Envelope

Every frame, both directions, is JSON text:

```json
{ "v": 2, "t": "<type>", "...": "type-specific fields" }
```

`v` is the protocol the sender speaks. `t` is the frame type.

## Handshake

The first client frame must be a `hello`:

```json
{
  "v": 2, "t": "hello",
  "proto": 2,
  "client": { "kind": "app", "version": "6.2.0" },
  "features": ["roster", "reactions"],
  "role": "listener", "clientId": "…", "name": "…"
}
```

| Field | Meaning |
| --- | --- |
| `proto` | Highest protocol the client speaks. **Required.** |
| `client.kind` | `app`, `bot`, `obs`, `web`, or `other`. Used for diagnostics, never for permissions. |
| `client.version` | Free text, 32 chars max. |
| `features` | Optional features the client understands (see below). |
| everything else | Room-specific (`role`, `clientId`, `name`, `key`). |

The server answers `welcome`, which now also says what was agreed:

```json
{ "v": 2, "t": "welcome", "proto": 2, "features": ["roster"],
  "server": { "protocol": 2, "minProtocol": 2 }, "...": "room snapshot" }
```

- `proto` is the lower of the two sides' versions.
- `features` is the intersection of what the client asked for and what the server offers. Only send
  or expect frames belonging to a feature that appears here.

### Update required

A client whose `hello` has no `proto`, or one below the server's `minProtocol`, gets exactly one
frame and then the socket closes with code **4426**:

```json
{ "v": 2, "t": "error", "code": "update_required",
  "message": "This version of Spectralis is too old to join. Update to the latest version to continue (needs protocol 2 or newer).",
  "minProto": 2, "proto": 2, "yourProto": null }
```

Clients must treat this as terminal: show `message`, **don't reconnect**. (The desktop app raises
`SharedPlayRoomSocket.UpdateRequiredReceived` and stops its retry loop.) Protocol 1 clients never
sent `proto`, so they land here.

## Rooms

| Room | Socket | What flows |
| --- | --- | --- |
| Shared Play | `/shared-play/v2/sessions/{code}/socket` | Full room state: playback, queue, roster, reactions, commands |
| Streamer Queue | `/streamer-queue/v2/rooms/{id}/socket` | **Change notifications only** (below) |

### Streamer Queue socket

Same envelope and handshake as above, no room-specific hello fields. The server answers:

```json
{ "v": 2, "t": "welcome", "roomKind": "streamer-queue", "proto": 2, "features": [],
  "server": { "protocol": 2, "minProtocol": 2 },
  "status": { "roomId": "…", "enabled": true, "acceptingSubmissions": true, "queueLength": 7,
              "nowPlaying": { "title": "…", "artist": "…" } } }
```

and then pushes:

| Frame | When |
| --- | --- |
| `sq.changed` | Anything about the room changed. Carries `rev` and the new public `status`. |
| `sq.submission` | A webhook / chat-bot submission was accepted. **Only with `sq.webhooks` negotiated.** Carries `source`, `displayName`, `submissionId`, `status`. |
| `pong` | Reply to a client `{ "t": "ping" }` |

The socket never carries private data (no submission list, no tokens), so every frame is safe to show
any viewer. A client that needs more, like the owner's dashboard wanting the submission list, reacts to
`sq.changed` by fetching over REST with its own credentials. That keeps permissions in one place.

**Keep polling as a fallback.** The socket is an accelerator, not the source of truth: the app polls every
10 s and the bot every 15 s regardless, and the socket just makes them refresh immediately. Collapse bursts
of `sq.changed` (the app and bot wait 400 ms) so approving ten items is one refresh, not ten.

## Features

Features are additive. A feature adds frames; it never changes the meaning of existing ones. Anything
that changes existing frames bumps the protocol version instead.

| Feature | Meaning | Gated by the server? |
| --- | --- | --- |
| `roster` | Shared Play roster frames (live listener list, presence) | No, announced for forward compatibility |
| `reactions` | Shared Play `reaction` frames | No, announced for forward compatibility |
| `commands` | Shared Play `command` frames (permitted listener commands relayed to the host) | No, announced for forward compatibility |
| `sq.webhooks` | Streamer Queue `sq.submission` frames for webhook / chat-bot submissions ([webhooks](streamer-queue-webhooks.md)) | **Yes**, only sent when negotiated |

Only list a feature in `SERVER_FEATURES` once something real sits behind it. The first three describe
frames the Shared Play hub already sends to everyone, so for now they just show up in `welcome`.

## The OBS overlay

The overlay is a local page the app itself serves (`ObsOverlayServer`), so it ships in the same build as
its server and doesn't use the websocket. It can still drift at runtime: OBS keeps a browser source's page
cached across app updates, so an old page can end up reading a newer state shape. So the state JSON carries
a `protocol` number (`ObsOverlayProtocol.Version`) and the page compares it with its own `PROTOCOL`. On a
mismatch it shows "out of date, refresh the browser source" and reloads once. A test fails if the two
numbers differ. Pages cached before this check existed can't be helped; that's a one-time refresh.

## Bumping the protocol

Where the number lives (grep for these when you change it):

| Where | What |
| --- | --- |
| `backend/src/protocol.rs` | `PROTOCOL_VERSION`, `MIN_PROTOCOL` |
| Desktop app | `SharedPlayDefaults.RealtimeProtocolVersion` (the Shared Play socket and `StreamerQueueRealtimeClient` both use it) |
| Web player | `ENVELOPE_V` in `web-share/player.js` |
| Discord bot | `REALTIME_PROTOCOL` in `discord-bot/src/lib/sqRealtime.ts` |
| OBS overlay | `ObsOverlayProtocol.Version` and `PROTOCOL` in `ObsOverlayHtml.cs` (separate numbering: it versions the local state JSON, not the websocket) |

1. Change `PROTOCOL_VERSION` (and, to cut off old clients, `MIN_PROTOCOL`) in `protocol.rs`.
2. Update every client constant in the table above.
3. **Ship the clients before raising `MIN_PROTOCOL` on the server.** Raising it first locks out every
   installed client until it updates, which is the point, but the update has to exist.

## Rolling this out

Deploying this backend change makes released app builds that predate protocol 2 show the update
message in Shared Play rooms. Release the app first, then deploy the backend.
