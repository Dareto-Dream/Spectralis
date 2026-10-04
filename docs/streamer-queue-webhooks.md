# Streamer Queue Webhook API

A chat-platform-agnostic way to feed a Streamer Queue room. Twitch, YouTube, Kick, a Mixitup
macro, a Streamer.bot action, a Discord bot, `curl`: if it can send an HTTPS request, it can
submit songs. The bundled [`chat-bridge`](../chat-bridge/) uses this API for Twitch and YouTube chat.

The webhook layer sits on top of the existing room submit path, so queue length, duplicate checks,
approval, per-person limits and the rest of the room's settings all apply unchanged.

## Auth

Each room can have one **webhook key**. Send it as a header:

```
X-Spectralis-Webhook-Key: wk_…
```

The key is shown once, when the room owner creates it:

```
POST /streamer-queue/v2/rooms/{roomId}/webhook/key
{ "ownerToken": "…", "action": "rotate" }      →  { "webhookKey": "wk_…" }
{ "ownerToken": "…", "action": "revoke" }      →  { "revoked": true }
```

Rotating invalidates the old key immediately. Keys are stored hashed; a lost key can't be
recovered, only rotated. A key can submit songs and read queue status. It can't change settings,
approve, reject or skip.

## Submit a song

```
POST /streamer-queue/v2/rooms/{roomId}/webhook/submit
X-Spectralis-Webhook-Key: wk_…

{
  "source": "twitch",            // lowercase slug of the platform, [a-z0-9_-]{1,16}
  "userId": "12345",             // the platform's stable user id (not the display name)
  "displayName": "viewername",
  "url": "https://youtu.be/…",   // http(s) or spotify:
  "title": "…", "artist": "…"    // optional
}
```

`source` + `userId` identify the person for rate limits and the per-person submission cap, so the
same viewer is the same person across reconnects and bots, and two platforms with the same numeric
id never collide.

Success:

```json
{ "submissionId": "…", "status": "queued", "position": 3 }
```

`status` is `queued`, or `pending_approval` when the room requires approval. Errors reuse the room's
messages (`The queue is full right now.`, `This track is already in the queue.`, …) with an HTTP
`400`; a bad key is `403`; a disabled room is `404`.

## Queue status

```
POST /streamer-queue/v2/rooms/{roomId}/webhook/status
X-Spectralis-Webhook-Key: wk_…
{ "source": "twitch", "userId": "12345" }
```

```json
{
  "acceptingSubmissions": true,
  "queueLength": 7,
  "nowPlaying": { "title": "…", "artist": "…" },
  "yourPositions": [2, 5]
}
```

`nowPlaying` is `null` when nothing is playing. `yourPositions` is 1-based and empty when the person
has nothing queued.

## Events echoed to the room

With the `sq.webhooks` feature negotiated on the [realtime socket](realtime-protocol.md), each
accepted webhook submission also produces a frame, so an OBS overlay can show where a request came
from:

```json
{ "v": 2, "t": "sq.submission", "source": "twitch", "displayName": "viewername",
  "submissionId": "…", "status": "queued" }
```
