# Spectralis Discord Bot — Planning

Status: **all commands implemented, not deployed**. Backend PIN pairing and the scoped
bot token both exist now (`backend/src/main.rs`, `StreamerQueueView`'s "Link Discord"
button). Nothing here talks to real Discord servers until a Discord application/bot
token is created and `.env` is filled in — see "Getting a bot token running" below.

## What it's for

One Discord bot process, invited into many streamers' servers. Each server (guild) can
link one or more channels to a Streamer Queue room (`backend/src/main.rs`'s
`/streamer-queue/v1/rooms/*` API — see [[project-streamer-queue]] in memory for how that
system works). Once linked, a channel gets:

- An auto-updating "now playing" post.
- `/queue` — read-only now-playing + up-next.
- `/request`, `/skip`, `/superskip` — the same actions viewers get on the `sq.html` web
  page, without leaving Discord.
- Streamer-only moderation commands (`/close-queue`, `/open-queue`) gated behind a
  scoped bot token (see below).

## The linking problem

The bot is one process serving unboundedly many guilds, each potentially with its own
queue (or multiple queues across different channels). Given a Discord interaction, the
bot only has `(guildId, channelId)` — it needs a reliable way to resolve that to
`(roomId, credential)`. Two bad options:

- **Paste the room's `ownerToken` into Discord.** Works, but `ownerToken` is a
  full-control secret (settings, Stripe Connect, delete room). Pasting it into a chat
  message — even briefly, even in a "private" channel — means it's now in Discord's
  logs and screenshot-able. Never do this.
- **Paste the raw `roomId` (a UUID) and trust whoever ran the command.** No proof the
  Discord user actually owns the room — anyone who finds/guesses a room ID could hijack
  a channel's link.

### Chosen approach: short-lived PIN pairing

1. In the Spectralis App (`StreamerQueueView`, where the owner already holds
   `ownerToken`), the **"Link Discord"** button calls
   `POST /streamer-queue/v1/rooms/{id}/discord-pin` with the `ownerToken`. The backend
   mints a 6-digit numeric PIN, writes it to `data/sq-discord-pins/<pin>.json` with a
   10-minute expiry, and returns it to the app for display (`DiscordPinDisplay` on
   `StreamerQueueViewModel`).
2. The streamer runs `/link-queue pin:123456` in whichever Discord channel they want
   linked.
3. The bot calls `POST /streamer-queue/v1/rooms/discord-pin/exchange` with `{ pin }`.
   The backend deletes the PIN file immediately (single-use, regardless of outcome),
   checks it hadn't expired, mints a new **scoped bot token**, stores it as
   `discordBotToken` on the room, and returns `{ roomId, botToken }`.
4. The bot writes `(guildId, channelId) -> { roomId, botToken }` into its own local
   SQLite store, starts polling that channel immediately, and confirms in-channel.

A PIN is cheap to leak (short-lived, single-use, guessable only 1-in-a-million within
its window) and never gives Discord — or the bot process — the actual owner secret.
This mirrors how Sonarr/Plex-style device pairing and Twitch chatbot linking work.

Unlinking (`/unlink-queue`) just removes the local row and stops that channel's poll
loop; no backend call — the bot token stays valid in case the channel gets re-linked
later without needing a fresh PIN. (No revocation endpoint exists yet; a leaked bot
token today has to be invalidated by hand by re-running the pairing, which overwrites
`discordBotToken` on the room.)

### Scoped bot token

Implemented in `backend/src/main.rs`:

- `discordBotToken: Option<String>` field on the room, set by
  `post_sq_discord_pin_exchange`.
- `sq_bot_token_valid()`, parallel to `sq_owner_token_valid()`.
- `put_sq_settings` now accepts either `ownerToken` (full access, unchanged) or
  `botToken` — but a bot-token caller is rejected if the payload touches `enabled`,
  `channelId`, or `settings`; it may only set `acceptingSubmissions`. Stripe and
  delete routes don't accept `botToken` at all.
- `post_sq_discord_pin` (owner-token gated) and `post_sq_discord_pin_exchange`
  (PIN-gated) issue and consume PINs.

## What talks to the backend today

- `GET /streamer-queue/v1/rooms/{id}` — now playing, queue length, settings (which
  tiers/fees are turned on). No token needed. Powers `/queue` and the now-playing
  poster.
- `POST /streamer-queue/v1/rooms/{id}/submit` — no token needed. Powers `/request`.
- `POST /streamer-queue/v1/rooms/{id}/submissions/{sid}/promote` — no token needed.
  Powers `/skip` / `/superskip` on a submission the same Discord user already made.
- `POST /streamer-queue/v1/rooms/discord-pin/exchange` — PIN-gated. Powers
  `/link-queue`.
- `PUT /streamer-queue/v1/rooms/{id}/settings` with `botToken` — scoped to
  `acceptingSubmissions` only. Powers `/close-queue` and `/open-queue`.

The public endpoints fingerprint submitters by `fpCookie`/`fpUa`/etc. (see
`build_fingerprint` in `backend/src/main.rs`) for per-person rate limiting and skip
promotion auth. The bot substitutes the Discord user ID as `fpCookie` so the same
person is recognized consistently across requests, and `fpUa: "discord-bot"` /
`fpTz: 0` as stable stand-ins (see `src/lib/sqApi.ts`).

## Data model (bot-local SQLite, `better-sqlite3`)

```sql
CREATE TABLE guild_links (
  guild_id   TEXT NOT NULL,
  channel_id TEXT NOT NULL,
  room_id    TEXT NOT NULL,
  bot_token  TEXT,              -- set by the PIN exchange; null only for a bad manual insert
  linked_by  TEXT NOT NULL,     -- Discord user ID who ran /link-queue
  linked_at  TEXT NOT NULL,     -- ISO 8601
  PRIMARY KEY (guild_id, channel_id)
);

CREATE TABLE submissions (
  discord_user_id TEXT NOT NULL,
  guild_id        TEXT NOT NULL,
  channel_id      TEXT NOT NULL,
  submission_id   TEXT NOT NULL,  -- SQ submission ID, for /skip lookups
  submitted_at    TEXT NOT NULL,
  PRIMARY KEY (discord_user_id, guild_id, channel_id)
);
```

A room can be linked into more than one channel/guild (e.g. co-streamers); a channel
can only ever point at one room. `submissions` remembers each user's most recent
request per channel so `/skip` and `/superskip` know which submission to promote
without asking the user to paste an ID.

## Commands (v1 slash commands)

| Command | Backend dependency | Status |
|---|---|---|
| `/link-queue pin:<6 digits>` | PIN exchange endpoint | implemented |
| `/unlink-queue` | none (local only) | implemented |
| `/queue` | public `GET room` | implemented |
| `/request url:<link>` | public `submit` | implemented |
| `/skip` | public `promote` | implemented (only on your own last request) |
| `/superskip` | public `promote` | implemented (only on your own last request) |
| `/close-queue` | scoped bot token | implemented |
| `/open-queue` | scoped bot token | implemented |

"Implemented" means the command code and backend route both exist and typecheck/build.
None of it has run against a live Discord bot token yet — see "Getting a bot token
running" below for what's left to actually turn it on.

`/link-queue` and `/unlink-queue` are restricted to members with the Discord
`Manage Channels` permission, so random viewers can't relink or unlink a channel even
though the PIN itself is the real credential check.

## Now-playing poster

One poll loop per **linked channel** (not per guild), polling
`GET /streamer-queue/v1/rooms/{id}` on the same 10–15s cadence the desktop app and
`sq.js` already use. On a `nowPlayingId` change, edit (or repost) a single pinned
embed in the channel rather than spamming a new message per track. Loops are staggered
on startup so a bot linked into hundreds of channels doesn't burst-request the backend
all at once. If this ever needs to scale past polling, the natural next step is a
webhook the backend calls on now-playing change instead — not needed at current scale.

## Folder layout

```
discord-bot/
  package.json
  tsconfig.json
  .env.example
  .gitignore
  PLANNING.md
  src/
    index.ts                 # client bootstrap, command registration, interaction routing
    commands/
      link-queue.ts
      unlink-queue.ts
      queue.ts
      request.ts
      skip.ts
      superskip.ts
      close-queue.ts
      open-queue.ts
    lib/
      db.ts                  # better-sqlite3 wrapper for guild_links / submissions
      sqApi.ts                # thin HTTP client mirroring Spectralis.Core/StreamerQueue/StreamerQueueClient.cs
      nowPlayingPoller.ts     # per-channel poll loop + embed updates
```

## Getting a bot token running

Nothing here can connect to real Discord servers without a Discord application. This
part can't be scripted — it needs a few clicks on Discord's site with your own
account:

1. Go to the [Discord Developer Portal](https://discord.com/developers/applications) →
   **New Application**.
2. **Bot** tab → **Reset Token** → copy it into `discord-bot/.env` as `DISCORD_TOKEN`
   (copy `.env.example` to `.env` first). Under **Privileged Gateway Intents**, nothing
   here needs any of the three toggles (Presence/Server Members/Message Content) — the
   bot only uses slash commands, so leave them off.
3. **OAuth2 → General** tab → copy the **Application ID** into `.env` as
   `DISCORD_CLIENT_ID`.
4. **OAuth2 → URL Generator** → scopes: `bot`, `applications.commands` → permissions:
   `Send Messages`, `Embed Links`, `Use Slash Commands` → open the generated URL to
   invite it into a test server.
5. Set `SQ_API_BASE_URL` in `.env` to wherever `backend/` is actually running (Railway
   URL in production, `http://localhost:8787` for local testing against a local
   backend).
6. `npm install && npm run dev` from `discord-bot/`. On startup it registers the slash
   commands globally (`registerCommands()` in `src/index.ts` — can take up to an hour
   to propagate the first time; Discord's per-guild command registration is instant if
   that matters while testing).

None of this is committed anywhere — `.env` is gitignored, as is `data/` (the bot's
SQLite file). Each person running the bot needs their own Discord application.

## Open questions (not decided yet)

- Should `/skip` and `/superskip` handle Stripe payment in-Discord (payment link via
  DM) or just tell the user to finish payment on the `sq.html` submit URL? Leaning
  toward the latter for v1 — Discord doesn't have a clean embedded card element like
  Stripe.js does.
- Should one room be linkable into multiple channels at once (e.g. `#requests` and
  `#now-playing` in the same server)? The data model already allows it; no UI decision
  made on whether `/link-queue` should warn if the room is already linked elsewhere.
- Rate limiting `/request` per Discord user — the backend's own
  `maxSubmissionsPerPerson` setting already covers this via the fingerprint cookie
  trick, so probably no bot-side limiting needed. Revisit if abuse shows up.
