# Creator Tools

Spectralis includes lightweight tools for building richer track assets without ever leaving the
player — I got tired of alt-tabbing to five different apps just to time some lyrics.

## Lyrics Timing Studio

Open **File → Lyrics Timing Studio...** or press `Ctrl+Shift+L`.

The studio can:

- Load plain lyric lines from pasted text or the current track's existing lyrics.
- Show the active playback position while audio continues playing.
- Play or pause the active track from inside the timing window.
- Tap the selected lyric line to the current playback position, then advance to the next line.
- Seek back to a timed line for quick checks.
- Nudge all timed lines by `0.10s` or `0.50s`.
- Copy or export an `.lrc` file.

When the current track is a local file, export defaults to a matching sidecar path such as
`track-name.lrc`. For streamed sources, Spectralis just asks you where to put it.

## Timeline Editor

Open **Tools → Creator → Timeline Editor** or press `Ctrl+Shift+T`. It edits the playing track's
[reactive timeline](formats/reactive-timeline.md) (`<track>.spectralis-reactive.json`, next to the audio
file) so you no longer hand-write the JSON.

- **Lanes and sections.** One lane per event target, sections along the top band. Events with a length
  are blocks (drag the right edge to resize); instant events are diamonds.
- **Editing.** Click to select, drag to move, double-click an empty lane to add an event, `+ Event` /
  `+ Section` / `Split` act at the playhead. The inspector edits target, action, time, length, easing and
  params (`key=value` per line; numbers and `true`/`false` are stored as such).
- **Snapping.** Snap to a grid (1/16 s to 1 s) and, if the track has a beat grid set in Now Playing, to
  beats too. Sections can't overlap: dragging one stops against its neighbours.
- **Undo.** `Ctrl+Z` / `Ctrl+Y`; a whole drag is one step.
- **Playback.** Click the ruler to seek; the playhead follows playback and the view scrolls with it.
  `Ctrl+wheel` zooms, `Fit` fits the whole track.
- **Saving.** `Ctrl+S` writes the sidecar (a temp file, then a move, so a crash can't leave a half-written
  file) and refuses a timeline the loader would reject. Switching tracks while you have unsaved edits keeps
  your edits until you save or reload.

## Lyric Explanations

Add contextual annotations to synced lyrics — basically Genius-style annotations, but yours.
Explanations appear below the current lyric line during playback.

Explanations are stored as timestamp-keyed JSON and can be embedded in two ways:

### Method 1: Sidecar `.lrc.json` File

Create a `.lrc.json` file next to your `.lrc` file:

```
track-name.mp3
track-name.lrc        ← synced lyrics
track-name.lrc.json   ← explanations
```

Format: timestamps (MM:SS.MS) map to explanation text.

```json
{
  "00:12.50": "Opening line sets the scene",
  "00:24.26": "Central metaphor about emptiness",
  "00:31.75": "Glass shards symbolize fragmentation"
}
```

**Timestamp format:** `MM:SS.MS` where MM=minutes, SS=seconds, MS=centiseconds.

### Method 2: ID3v2 Tag Embedding

Embed explanations directly in the audio file's ID3v2 metadata using a metadata editor like
**foobar2000** (whic is what I use, no affiliation, it's just good).

1. Open the `.mp3` file in a metadata editor.
2. Add a custom text frame:
   - **Frame type:** User Text Information Frame (TXXX)
   - **Description:** `LYRIC_EXPLANATIONS`
   - **Value:** Paste the same JSON object as above

This method travels with the audio file and wins if both sidecar and embedded explanations exist.

### Best Practices

- Match timestamps exactly to your LRC file (e.g., if LRC has `[00:12.50]`, use `"00:12.50"` in JSON).
- Keep explanations concise (1-2 sentences).
- Escape special characters in JSON: use `\"` for quotes and `\\` for backslashes.
- Validate JSON with an online validator before embedding.

---

## Content Warnings

Content warnings (TWs) let you attach short labels to individual local tracks. When a track
with warnings is about to play, a pre-play popup lists the tags and makes the listener confirm
before audio starts — I'd rather be a little annoying here than surprise somebody.

### Setting warnings

1. Open the queue panel.
2. Right-click any local file track.
3. Choose **Content Warnings...**.
4. Enter labels separated by commas — for example: `violence, flashing lights, loud sounds`.
5. Click **Save**.

The menu item shows a **✓** suffix when a track already has warnings configured. To remove
all warnings for a track, open the same dialog and click **Clear**.

### Pre-play popup

Every time Spectralis is about to play a track that has warnings — whether triggered by the
user, queue auto-advance, or a previous/next navigation — a modal popup appears with:

- The list of TW labels as styled chips.
- **Play Anyway** — dismisses the popup and starts playback.
- **Cancel** / Escape — aborts playback for this track without advancing the queue.

### Storage

Warnings are stored in `%LocalAppData%\Spectralis\content_warnings.json` as a flat
`{ "c:\\path\\to\\file.mp3": ["tag1", "tag2"] }` dictionary keyed by the normalized
(lowercased, fully qualified) file path. It's plain human-editable JSON, hand-edit it if you want.

### Scope

Content warnings apply to **local file tracks only**. Spotify, YouTube, SoundCloud, Suno,
and shared-queue URL pointers aren't affected, at least not yet.
