# Spectralis chat bridge

Lets viewers request songs from **Twitch** and **YouTube** live chat, next to the Discord bot. It
doesn't talk to the queue directly: it turns chat lines into calls to the
[Streamer Queue webhook API](../docs/streamer-queue-webhooks.md), so a room's approval rules, limits
and duplicate checks apply exactly as they do for the web page and Discord. Anything else that can
send an HTTPS request (Streamer.bot, Mixitup, `curl`) can use the same API without this package.

## Chat commands

| Command | What it does |
| --- | --- |
| `!sr <link>` (also `!request`, `!songrequest`) | Adds the link to the queue |
| `!queue` (`!q`) | Queue length and your positions |
| `!song` (`!np`, `!nowplaying`) | What's playing now |

Everything else in chat is ignored. The prefix and a per-person cooldown are configurable.

## Setup

1. In the Spectralis app, open the Streamer Queue room and create a **webhook key**
   (`POST /streamer-queue/v2/rooms/{id}/webhook/key`, see the webhook doc). It's shown once.
2. Set the environment variables below and run it. It's a plain Node (22.4+) process with no
   runtime dependencies, so it runs fine as its own Railway service.

```bash
npm install        # dev tooling only (typescript, node types)
npm test
npm run build && npm start
# or, for development: npm run dev
```

| Variable | Required | |
| --- | --- | --- |
| `SQ_API_BASE_URL` | yes | Backend origin, e.g. `https://api.example.com` |
| `SQ_ROOM_ID` | yes | The Streamer Queue room id |
| `SQ_WEBHOOK_KEY` | yes | The webhook key from step 1 |
| `TWITCH_CHANNEL` | one of Twitch/YouTube | Channel to listen to (without `#`) |
| `TWITCH_NICK`, `TWITCH_OAUTH` | no | Bot account that lets the bridge answer in Twitch chat. Without them it joins anonymously and only listens |
| `YOUTUBE_API_KEY` | one of Twitch/YouTube | YouTube Data API v3 key |
| `YOUTUBE_VIDEO_ID` or `YOUTUBE_LIVE_CHAT_ID` | with the YouTube key | The live stream to follow |
| `CHAT_PREFIX` | no | Command prefix, default `!` |
| `CHAT_COOLDOWN_SECONDS` | no | Gap between one person's commands, default `5` |

## Notes

- **YouTube is read-only.** Posting to YouTube chat needs OAuth on the channel, which this bridge
  doesn't ask for, so replies only happen on Twitch. Each YouTube poll costs a few quota units and the
  API sets the poll interval; a multi-hour stream stays well inside the default daily quota.
- Viewers are keyed on the platform's stable user id, not their display name, so renaming doesn't
  dodge the per-person limit and a Twitch id never collides with a YouTube one.
- Messages published before the bridge started are never replayed.
