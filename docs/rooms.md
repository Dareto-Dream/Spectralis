# Shared Play Rooms

Shared Play has two kinds of room.

| | Temporary | Permanent |
|---|---|---|
| Who gets one | An app that is **not** signed in to Ward | Every Ward account, **one each** |
| How it is made | Starting a party creates a session | Created the first time a signed-in account starts a party |
| Who can join | Only people given the invite link | Anyone, or approved people, depending on the room's access setting |
| Listed anywhere | Never | In the room finder when set to public; otherwise private |
| Lifetime | Gone when the host stops, or after 12 hours | Stays, with a stable address, between sessions |
| Address | `https://player.deltavdevs.com/sessions/{code}` | `https://player.deltavdevs.com/rooms/{id}` |

What used to be called a *channel* is just a permanent room. Streamer Queue rooms are a separate
product and don't count toward the one-room limit.

---

## In the app

The Shared Play screen has one card that changes with who you are.

- **Signed out: "Temporary room".** Start makes a private, link-only party. The invite link appears
  while it is live. The card offers Sign in with Ward for a permanent room.
- **Signed in: "Your room".** Start runs the party in your permanent room, creating it the first time.
  A switch lists the room in the room finder or takes it off. Room settings opens branding and access
  on the Player site, and the room's permanent link can be copied any time.

Host controls (what listeners can do, the listener list, activity) are folded away until needed, and
listener requests only show when there are some.

Code: `SharedPlayAccount.cs` (the account and room state), `SharedPlayView.axaml`.

---

## In the backend

Code: `backend/src/rooms.rs`.

- **One permanent room per account.** `POST /player/v1/rooms` with `kind: "channel"` answers `409`
  (naming the room the account already has) if it owns one. Streamer-queue rooms are exempt.
- **`GET /player/v1/me/room`** returns the account's room or `{ "room": null }`.
- **Sessions know what they are.** `POST /shared-play/v2/sessions` records the owner when the caller
  is signed in and marks the session `temporary: true` when not. Both the response and the manifest
  carry the flag. A temporary session has no owner and is never returned by any listing.
- **The room finder** (`GET /player/v1/rooms`) returns only permanent rooms whose `isPublic` is true.
  Turning a room public or private is `PUT /player/v1/rooms/{id}/profile` with `isPublic`.
- **A permanent room goes live** when the host's app starts a session and keeps its channel pointer
  fresh (`PUT /shared-play/v2/channels/{id}`); the pointer lapses 30 seconds after the heartbeat
  stops, which is how a room shows as live or between sessions.

---

## Wiping the old rooms

Rooms made before this model could be many per account and were all called channels. To start clean:

```powershell
$headers = @{ Authorization = "Bearer $env:SPECTRALIS_ADMIN_TOKEN" }
$body = '{"confirm":"wipe all rooms"}'
$result = Invoke-RestMethod -Method Post -Headers $headers -ContentType 'application/json' -Body $body `
  https://spectralis-api.deltavdevs.com/spectralis/v1/admin/rooms/wipe
$result.backup | ConvertTo-Json -Depth 20 | Set-Content rooms-backup.json
```

`POST /spectralis/v1/admin/rooms/wipe` needs the admin token and that exact phrase. It reads every
permanent room and every admission record first and only then deletes them, along with their artwork,
so a failure part-way leaves nothing half-deleted. It returns counts and the deleted records as a
backup. The backup contains each room's owner token, so keep it private. Streamer Queue data and
unrelated records are untouched, and each account can make its one room again straight away.
