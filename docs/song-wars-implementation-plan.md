# Song Wars Implementation Plan

Song Wars is a live tournament mode for Spectralis where a host runs a bracket, streams each song
to an external audience, and collects judge decisions in a separate private voting surface. The
first implementation should focus on host control, judge voting, match timing, and bracket state.
Community voting is intentionally out of scope for this pass; later community mode can consume
third-party poll results from Twitch, Kick, or another polling provider.

The important product split:

- Judges listen to the host's live stream by default, not through the voting interface.
- The judge voting interface can offer optional Shared Play listening for judges who explicitly
  opt in, but voting must work without it.
- Community voting is separate and should not be modeled as judge voting. Treat it as a future
  poll-ingestion adapter layer.

---

## Goals

- Add a Song Wars tournament mode for 32 song submissions.
- Support a fixed judge panel of 1-5 judges.
- Run double-elimination bracket flow in one sitting.
- Let the host present and stream songs from Spectralis.
- Let judges submit hidden Pass / Fail / Eliminated decisions during voting windows.
- Reveal a result when all judges submit or when the vote timer expires.
- Enforce winners bracket, losers bracket, elimination cap, and no-back-to-back-elimination rules.
- Use Shared Play where it helps with distribution, current-track state, queue preparation, and
  optional judge listening.
- Keep third-party community polling out of the judge voting implementation.

## Non-Goals For First Pass

- No Twitch/Kick chat vote parser.
- No community poll creation or vote counting.
- No public audience voting UI.
- No weighted judge scoring.
- No artist account system.
- No anti-cheat beyond basic room tokens and hidden votes.
- No automatic stream software control beyond existing OBS/shared overlay surfaces.

---

## Existing Pieces To Reuse

### Shared Play

Shared Play already provides:

- Host-created listen sessions.
- Public join URLs and permanent Live Channel links.
- Current playback state publishing.
- Queue snapshots.
- Upcoming local track preparation.
- Browser listener presence.
- Reactions and channel stats.

Song Wars should reuse this for:

- Optional judge listening links.
- A stable "currently playing match track" pointer.
- Preloading Track 2 while Track 1 is playing when possible.
- Optional public listen link if the host wants a Spectralis-backed stream companion.

Song Wars should not depend on Shared Play for voting. Voting must remain reliable even when a judge
chooses to listen through OBS, Twitch, Kick, Discord, or any other stream path.

### Queue

The existing queue model can stage match songs. Song Wars should create and own a temporary match
queue rather than mutating the user's normal queue blindly. The host should be able to push the
current match into playback in order:

1. Track 1
2. Track 2
3. Voting window
4. Optional elimination vote window
5. Result reveal

### Backend

The Rust backend already stores Shared Play state in JSON files under session folders. Song Wars can
start with the same pragmatic persistence style:

- JSON-backed tournament state.
- Short-lived host and judge tokens.
- No database requirement for v1.

If Song Wars becomes a hosted public feature, the JSON store can later be migrated to a real
database without changing the desktop app's tournament state machine too much.

---

## Core Concepts

### Tournament

A tournament owns:

- Tournament ID.
- Name.
- Mode: `judges` for v1.
- Submission list.
- Seed list.
- Bracket state.
- Match order.
- Judge roster.
- Current match ID.
- Elimination count.
- Last match eliminated flag.
- Audit log.
- Created/updated timestamps.

### Submission

A submission represents one song in the bracket:

- Submission ID.
- Display title.
- Artist display name.
- Optional artist rank/history.
- Local file path or external source pointer.
- Duration metadata.
- Eligibility/status: active, losers bracket, eliminated, withdrawn.
- Seed.

### Match

A match owns:

- Match ID.
- Bracket: winners, losers, grand finals.
- Round index and display label.
- Slot A submission ID.
- Slot B submission ID.
- Playback state: pending, track A playing, track B playing, voting, elimination voting, complete,
  skipped.
- Winner submission ID.
- Loser submission ID.
- Result: pass, fail, eliminated, skip.
- Vote snapshots.
- Started/completed timestamps.

Interpretation of Pass / Fail:

- Winners bracket: Pass advances the focused/winning song in winners path, Fail sends it to losers
  path, Eliminated removes it entirely.
- Losers bracket: Pass advances, Fail eliminates.
- The host UI should phrase the decision relative to the presented matchup so judges are never
  guessing which side Pass or Fail applies to.

### Judge

A judge owns:

- Judge ID.
- Display name.
- Join token.
- Optional listen preference: stream only, Shared Play opt-in.
- Current match vote state.
- Last seen timestamp.

### Vote

A vote owns:

- Judge ID.
- Match ID.
- Phase: primary or elimination.
- Choice: pass, fail, eliminated.
- Submitted timestamp.
- Revision number.

Votes are hidden until reveal. The host can see completion count, not individual choices, before
the reveal.

---

## Tournament Rules

### Setup

- 32 submissions.
- Double elimination.
- Fixed judge panel of 1-5 judges.
- Single sitting.
- Known artists are seeded by historical performance.
- Unknown artists are randomly placed.
- Tournament mode is locked before bracket start.

### Timing

- Track 1: up to 4 minutes.
- Track 2: up to 4 minutes.
- Primary voting window: 2 minutes.
- Optional elimination vote window: 2 minutes.
- Standard match target: 10 minutes.
- Match with elimination vote target: 12 minutes.

The host can manually advance a track early, pause timers, restart a timer, or mark a stream mishap.
Manual overrides must be written to the audit log.

### Judge Counts

- 1 judge: direct decision.
- 2 judges: majority required; 1-1 tie skips the match.
- 3 judges: 2+ votes required.
- 4 judges: 2+ votes required; 2-2 tie skips the match.
- 5 judges: 3+ votes required.

### Winners Bracket Choices

- Pass: advance in winners bracket.
- Fail: drop to losers bracket.
- Eliminated: out of tournament entirely.

Winners bracket safeguards:

- Maximum 9 direct eliminations.
- No direct eliminations in consecutive matches.
- If Eliminated is unavailable because of safeguards, hide or disable it with a clear host-side
  reason.

### Losers Bracket Choices

- Pass: advance in losers bracket.
- Fail: eliminated from tournament.
- No Eliminated option; the song is already in a one-loss path.

### Tiebreakers

Even judge count:

- Exact tie means Skip.
- Skipped matches are postponed to the end of the current bracket round and replayed as the final
  match of that round.

Odd judge count in winners bracket:

- If Eliminated has a clear majority, Eliminated wins.
- If Eliminated does not have majority, convert Eliminated votes to Fail and re-tally.

Losers bracket:

- Same majority and tie handling, without an Eliminated option.

### Result Reveal

Reveal happens when either condition is met:

- All judges submit.
- The active voting timer expires.

The reveal should show:

- Final choice.
- Judge count by choice.
- Which rule resolved the outcome, such as majority, tie skip, or eliminated-to-fail conversion.
- Resulting bracket action.

---

## UX Surfaces

### Host Console

Add a Song Wars host console in the desktop app. It should include:

- Tournament setup wizard.
- Submission import/editor.
- Seed preview.
- Bracket overview.
- Current match control.
- Playback buttons for Track 1 and Track 2.
- Timers for track and vote phases.
- Judge connection/completion status.
- Result reveal panel.
- Manual override controls.
- Shared Play / Live Channel status and copy buttons.

The host console is the source of truth for match progression. Playback follows the host console,
not judge pages.

### Judge Voting Page

The judge page should be minimal and private:

- Join by judge token.
- See tournament name and current match label.
- See Track 1 / Track 2 labels and current phase.
- Submit vote during the voting window.
- Change vote until reveal, if allowed by host setting.
- See "vote received" state.
- See revealed result after voting closes.
- Optional "Listen in Spectralis Shared Play" button or toggle.

The page must not autoplay Shared Play audio. Judges who opt into Shared Play should explicitly
activate playback because browser autoplay rules and stream-delay differences can otherwise create
confusion.

### Stream Overlay

Keep this separate from judge voting. Useful overlay widgets:

- Current match.
- Track A / Track B now-playing.
- Timer.
- Bracket round.
- Result reveal.
- Elimination count.
- Upcoming match.

Avoid showing judge votes before reveal.

### Community Polling Placeholder

The host console can reserve a disabled "Community Mode" or "Poll Source" section, but it should
say the first pass is judge mode only. Do not wire this into judge vote models.

---

## Backend Contract

Add Song Wars routes under a separate namespace:

```txt
POST /song-wars/v1/tournaments
GET  /song-wars/v1/tournaments/{tournamentId}
PUT  /song-wars/v1/tournaments/{tournamentId}
POST /song-wars/v1/tournaments/{tournamentId}/start
POST /song-wars/v1/tournaments/{tournamentId}/matches/{matchId}/phase
POST /song-wars/v1/tournaments/{tournamentId}/matches/{matchId}/votes
GET  /song-wars/v1/tournaments/{tournamentId}/judge/{judgeToken}
POST /song-wars/v1/tournaments/{tournamentId}/judge/{judgeToken}/votes
POST /song-wars/v1/tournaments/{tournamentId}/matches/{matchId}/reveal
POST /song-wars/v1/tournaments/{tournamentId}/matches/{matchId}/override
```

Optional later:

```txt
POST /song-wars/v1/tournaments/{tournamentId}/polls/import
POST /song-wars/v1/tournaments/{tournamentId}/polls/webhook/{provider}
```

### Token Model

- Host token: can mutate tournament, phase, match, reveal, and overrides.
- Judge token: can read judge-safe state and submit only that judge's vote.
- Public token/link: can read stream overlay state only, if enabled.

Do not expose host tokens in Shared Play links.

### State Files

Initial JSON-backed layout:

```txt
backend/data/song-wars/{tournamentId}/tournament.json
backend/data/song-wars/{tournamentId}/judges.json
backend/data/song-wars/{tournamentId}/votes/{matchId}.json
backend/data/song-wars/{tournamentId}/audit.jsonl
```

Use atomic writes, matching the existing backend JSON style.

---

## Desktop App Implementation

### New Folders

```txt
SongWars/
  SongWarsModels.cs
  SongWarsBracketEngine.cs
  SongWarsVoteTally.cs
  SongWarsTournamentStore.cs
  SongWarsBackendClient.cs
  SongWarsSessionController.cs

Forms/
  SongWarsHostDialog.cs
  SongWarsSetupDialog.cs
```

### State Machine

Recommended phase enum:

```txt
Setup
Ready
TrackAPlaying
TrackBPlaying
PrimaryVoting
EliminationVoting
Reveal
Complete
Skipped
Paused
```

The controller should own transitions and reject invalid ones. UI buttons should call controller
methods instead of mutating match state directly.

### Shared Play Integration

When a Song Wars match starts:

1. If Shared Play is enabled, ensure the session/live channel is active.
2. Publish Track A as the current Shared Play track.
3. Prepare Track B as the upcoming track if it is a local file.
4. Publish queue state with both match tracks.
5. On Track B start, activate the prepared package if available.
6. During voting, keep playback state available but do not require judges to listen there.

The judge page should receive Shared Play URL metadata from Song Wars state, but only render it as an
opt-in option.

### Host Playback

The host console should use existing playback code paths:

- Local files: load directly.
- External URLs: use existing URL resolvers where available.
- Queue pointers: use Shared Queue pointer behavior where appropriate.

Track cap behavior:

- Default to stopping or fading at 4:00.
- Let the host extend/skip manually.
- Record manual action in audit log.

---

## Bracket Engine

Implement bracket generation separately from UI. It should support:

- 32-entry initial seeding.
- Winners round order.
- Losers round order.
- Grand finals reset.
- Skipped match requeue at end of current bracket round.
- Direct elimination from winners bracket.
- Losers bracket elimination on Fail.

Round order:

1. Winners Round 1
2. Losers Round 1
3. Winners Round 2
4. Losers Round 2
5. Winners Round 3
6. Losers Round 3
7. Winners Semis
8. Losers Semis
9. Winners Finals
10. Losers Finals
11. Grand Finals

The implementation should not hard-code UI labels into bracket logic. Store machine-readable round
IDs and generate labels at the presentation layer.

---

## Vote Tally Engine

Create a pure tally service with deterministic tests.

Inputs:

- Bracket type.
- Judge count.
- Submitted votes.
- Voting phase.
- Eliminations used.
- Previous match direct-eliminated flag.
- Timer expired flag.

Outputs:

- Outcome: pass, fail, eliminated, skip, pending.
- Counts by choice.
- Rule applied.
- Whether elimination vote is needed.
- Whether choice was converted.
- Host-facing explanation.

Tests should cover:

- 1 judge direct decisions.
- 2 judge ties.
- 3 judge majority.
- 4 judge 2-2 tie.
- 5 judge majority.
- Winners eliminated majority.
- Winners eliminated-to-fail conversion.
- Losers bracket without Eliminated.
- Elimination cap.
- No back-to-back direct elimination.
- Timer expiry with missing judge votes.
- All-judges-submitted reveal.

---

## Implementation Phases

### Phase 1: Local Tournament Core

- Add Song Wars models.
- Add bracket engine.
- Add vote tally engine.
- Add local JSON tournament store.
- Add unit tests for bracket and tally rules.

### Phase 2: Host Console

- Add setup flow.
- Import/edit 32 submissions.
- Generate bracket.
- Drive current match phase state.
- Control playback for Track A and Track B.
- Add timer controls.
- Show result reveal and next match action.

### Phase 3: Judge Voting Backend

- Add backend Song Wars routes.
- Add host/judge token model.
- Store hidden votes server-side.
- Expose judge-safe state.
- Add reveal endpoint.
- Add audit log.

### Phase 4: Judge Voting Page

- Build private judge voting page.
- Add vote submit/change flow.
- Add connection and vote-received states.
- Add optional Shared Play listen control.
- Keep listening disabled by default.

### Phase 5: Shared Play Bridge

- Publish match track state through Shared Play.
- Preload Track B.
- Add Live Channel copy/link affordance in host console.
- Include Shared Play opt-in URL in judge-safe state.

### Phase 6: Overlay And Polish

- Add stream overlay state endpoint.
- Add overlay page/widgets.
- Add host override tools.
- Add recovery flows for skipped matches, missing files, judge disconnects, and stream mishaps.

### Phase 7: Community Voting Later

- Add poll provider abstraction.
- Add Twitch/Kick/manual poll import adapters.
- Add community-mode tally rules.
- Keep community mode separate from judge vote storage.

---

## Open Questions

- For a matchup, should judges vote on "which song wins" directly, or should Pass / Fail be framed
  as the second track challenging the first track?
- Should judges be allowed to revise a vote until reveal, or should first submission lock?
- Should skipped matches replay both tracks or jump straight to a fresh voting window?
- Should the host be able to replace a missing judge mid-tournament?
- Should the judge page show artist names before voting, or can the host hide identity for blind
  judging?
- Should direct Eliminated in winners bracket require a primary Fail threshold first, or is the
  Eliminated option always available until safeguards block it?

---

## Acceptance Criteria

- A host can create a 32-song judge-mode tournament.
- A host can run a match through Track 1, Track 2, voting, reveal, and bracket advancement.
- Judges can vote privately from separate judge pages.
- Results are hidden until all judges submit or the timer expires.
- Tiebreakers and Eliminated conversion match the locked rules.
- Winners and losers bracket transitions are deterministic and tested.
- Shared Play listening is optional for judges and never required for voting.
- Community voting remains explicitly separate and unimplemented in v1.
