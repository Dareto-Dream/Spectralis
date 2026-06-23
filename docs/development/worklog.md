# Spectralis Hardening Sprint — WORKLOG

**Branch:** `hardening/sprint-01`
**Base commit:** `f0d597a` — wip: pre-sprint working state baseline
**Goal:** Robustness pass on the Zero capsule and app/docs surfaces; keep playback reliable, shell quiet, track experience expressive.

---

## Sprint Status

- [x] **Iter 1** — `characterFor()` derived from `CHAR_SEGMENTS` (DRY, desync-proof)
- [x] **Iter 2** — Dead CSS `#effect-layer` cleanup in visualizer
- [x] **Iter 3** — Manifest canonical path documentation
- [~] **Iter 4** — URL audio source investigation (Audiomack blocked by OAuth; deferred)
- [x] **Iter 5** — Visualizer robustness: post-song active guard + RAF cleanup
- [x] **Iter 6** — zero_module.json dataRefs cleanup
- [x] **Iter 7** — Post-song fade-out grace window (3 s delay)
- [x] **Docs** — Full docs/ system created; `.spectral` album world format documented

---

## Completed Iterations

### Iter 1 — characterFor() derived from CHAR_SEGMENTS
**What changed:** `zero_visualizer.html` (both copies: `metadata/zero/` and `metadata/zero/capsule/assets/html/`).
Previously `characterFor()` was a 30-branch if-chain that manually re-encoded the same time→character
mapping already defined in `CHAR_SEGMENTS`. Any future edit to the segment list required updating
two places with no compiler or test to catch drift.

**Change:** Replaced `characterFor()` body with a backward-scan over `CHAR_SEGMENTS` identical to
the pattern already used in `updateSingerTag()`. Both callers now derive from the same source of truth.

**Verified:** Compared CHAR_SEGMENTS array entries against the old if-chain line by line — all 30
boundaries and character names match. Visual output is identical; the function is O(n) linear scan
over 30 items, called per-word during `buildWordModel()` at startup only (not per-frame).

**Risk removed:** Segment list + characterFor() could drift silently on any future LRC timing change.

---

### Iter 2 — Dead CSS `#effect-layer` cleanup
**What changed:** Both visualizer HTML files.
`#effect-layer` was listed in a shared CSS property rule but the element was never created in the DOM.
Removing it shrinks the selector and eliminates the dangling reference.

**Verified:** `diff` of both copies confirmed FILES IN SYNC. `grep` for `#effect-layer` returns no matches.

**Risk removed:** Stale CSS selectors that could confuse a future DOM audit or tooling pass.

---

### Iter 3 — Manifest canonical path documentation
**What changed:** `metadata/zero/zero_manifest.json` — added a `_source` comment field.
**Context:** Both `metadata/zero/zero_manifest.json` and `metadata/zero/capsule/manifest.json` are
byte-for-byte identical. The canonical source is `capsule/manifest.json` (the file inside the
capsule directory). `metadata/zero/zero_manifest.json` is a convenience copy kept in sync manually.

---

### Iter 5 — Visualizer post-song active guard + RAF cleanup
**What changed:** Both visualizer HTML files (capsule and standalone copies).

Previously `applyFrame()` ignored the `active` field from the Spectralis bridge. When playback
ended, the Delta sprite stayed frozen and fully visible on screen, and the `requestAnimationFrame`
loop kept running indefinitely consuming CPU.

**Changes:**
- Added `hostActive = true` and `inactiveAt = null` state variables
- `applyFrame()` now reads `source.active !== false` and timestamps the first inactive frame
- `render()` fades `#stage` opacity from 1→0 over 1.8s after deactivation, then stops
  rescheduling RAF — loop terminates, CPU usage drops to zero after song end
- Resuming playback (`active=true`) restores opacity and RAF restarts from `boot()` or the next
  frame signal (no manual restart needed — `applyFrame` sets opacity back to 1 inline)

**Verified:** Both files identical via `diff`. State machine:
- `active=true` (normal play): `hostActive=true`, stage opacity=1, RAF running → no change
- First `active=false` frame: `hostActive=false`, `inactiveAt` set, fade starts
- `alpha > 0` (fading): RAF continues each frame, decrementing opacity
- `alpha == 0` (fully faded): `return` without `requestAnimationFrame` → loop stops
- Next `active=true` signal: `inactiveAt=null`, `stage.opacity=1`, normal RAF resumes

**Risk removed:** Frozen post-song visuals; RAF loop burning CPU after track ends.

---

### Iter 6 — zero_module.json dataRefs cleanup
**What changed:** `metadata/zero/capsule/assets/data/zero_module.json`

Five binary image refs (`zeroPhoto`, `zeroThumb`, `pen`, `penHead`, `delta`) were listed in
`dataRefs` alongside the genuine text refs. `dataRefs` is for text assets resolved via
`delta-data-json:` — binary images are resolved through the manifest's `binaryAssets`, not the
module JSON. Removed the five image refs, keeping only `lrc`, `penXml`, `penHeadXml`.

**Verified:** Python `json.load` confirms valid JSON; all three kept refs are text assets;
the five removed refs are correctly present in `capsule/manifest.json → binaryAssets`.

**Risk removed:** Misleading module descriptor; future tooling that validates dataRefs as
text-only would incorrectly flag this capsule.

---

### Iter 7 — Post-song fade-out grace window (3 s delay)
**What changed:** Both visualizer HTML files.

The initial active guard faded out immediately on `active=false`. Since both pause and song-end
emit `active=false`, pausing mid-track would start the 1.8s fade-out. Added a 3-second hold
before the fade begins. Normal pause/resume (< 3 s) produces no visible change. Song-end and
sustained stop (≥ 3 s) trigger the fade.

---

## Open Risks

- `metadata/specials/zero/` (separate older zero visualizer) is unrelated to the capsule.
- Git lock files on the 9p mount require HEAD to be updated via direct file write rather than
  `git checkout` — this is a sandbox limitation only, doesn't affect the repo on the host.
- Other track module JSONs (meteor-shower, my-thoughts, SOS) also have `cover` in `dataRefs`
  but these are pre-pack loose manifests, not shipped capsules — benign for now.
- `posted_creator_key.json` has UTF-8 BOM; C# handles this transparently but may trip Python
  tooling. Low priority since the file is only used by the capsule-builder script.

---

## Validated (no change needed)

- `suppressAppLyrics: true` is properly handled: `IsAppLyricsAvailable()` returns false,
  lyrics panel hidden, technical line shows "Visualizer lyrics".
- `EmbeddedContentControl.SyncAudioFrame` already passes `active = activePlayback` in every
  frame — the C# bridge was correct; only the JS side needed the guard.
- `engine.IsPlaying` + `CheckAutoAdvance()` reliably detect natural track end via
  `position >= length - 0.25f` threshold; queue advance fires correctly.
- Zero capsule SHA256 matches manifest: `80bdbe16...c48d60`. Audio entry verified.
- All four capsule capabilities (`webview.localContent`, `visualizer.multiLayer`,
  `sharedPlay.hostCapsule`, `sharedPlay.packageUpload`) are valid per CDN contract spec.

---

## Next Candidates (if sprint continues)

1. Apply `hostActive` guard to other visualizers that use internal RAF loops.
2. Strip UTF-8 BOM from `posted_creator_key.json` so Python tooling parses it cleanly.
3. Fix `cover` in `dataRefs` of non-capsule module JSONs (meteor-shower, my-thoughts, SOS).
4. Investigate adding a new URL audio source (need a service without OAuth gating).
