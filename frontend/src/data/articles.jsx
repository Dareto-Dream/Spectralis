import { Package, Zap, MonitorPlay, Users, Layers, FileCode2, NotebookPen } from 'lucide-react'

// Curated write-ups adapted from docs/ for the public Learn section.
// Keep these in sync with docs/formats/*.md, docs/creator-tools.md when those change.
export const ARTICLES = [
  {
    slug: 'spectralis-capsule',
    icon: Package,
    label: 'Artist format',
    ext: '.spectralis',
    title: 'Inside a .spectralis capsule',
    summary: 'A 104-byte signed header glued to a ZIP archive — audio, art, lyrics, a reactive timeline, and WASM visualizers, all cryptographically verified.',
    body: (
      <>
        <p>
          A capsule is a 104-byte signed header glued to the front of a plain ZIP archive.
          The header is <code>magic (4) + format version (4) + Ed25519 public key (32) +
          Ed25519 signature (64)</code> — the signature covers every byte of the ZIP payload
          that follows, so a single flipped bit anywhere in the archive invalidates the whole file.
        </p>
        <p>
          Inside the ZIP: a required <code>manifest.json</code> and <code>audio/&lt;entry&gt;</code>,
          plus optional <code>reactive.json</code>, cover art under <code>assets/images</code>, and
          LRC lyrics under <code>assets/data</code>. Unknown creator keys trigger a one-time trust
          prompt in the app; revoked keys are rejected outright, every time.
        </p>
        <p>
          Capsules can also ship a click-through <strong>story explainer</strong> — either a fully
          custom HTML/CSS/JS page with the same <code>window.spectral</code> playback bridge
          embedded experiences get, or a synthesized pager built straight from a <code>story.pages</code>
          list in the manifest. If a capsule's own visualizer already renders lyrics, setting
          <code>suppressAppLyrics: true</code> stops Spectralis's lyrics panel from fighting it for
          screen space.
        </p>
      </>
    ),
    visual: {
      kind: 'code',
      filename: 'release.spectralis',
      code: `[4]  Magic .............. SPCC
[4]  Format version ..... 3
[32] Ed25519 public key
[64] Ed25519 signature
     ↓ ZIP payload
     manifest.json
     audio/track.flac
     reactive.json
     assets/images/cover.png
     assets/data/lyrics.lrc`,
    },
  },
  {
    slug: 'album-worlds',
    icon: Layers,
    label: 'Album format',
    ext: '.spectral',
    title: 'Album worlds: .spectral capsules',
    summary: 'A multi-track sibling to .spectralis that ships a whole album with an interactive HTML "world" the creator fully controls.',
    body: (
      <>
        <p>
          Where a <code>.spectralis</code> capsule ships one track with one optional experience,
          a <code>.spectral</code> capsule ships a whole album with an interactive HTML world the
          creator fully controls — a level-select map where each song is a level, interactive
          liner notes, a branching narrative, whatever the creator wants to build. Ship no world
          and the player just falls back to a plain tracklist.
        </p>
        <p>
          The binary envelope is identical to <code>.spectralis</code> with one exception: different
          magic bytes (<code>SPAC</code> instead of <code>SPCC</code>). That means the same signing
          tool, CDN key infrastructure, and trust dialog used for single-track capsules work unchanged
          for albums — trust decisions for both formats live in one shared local store.
        </p>
        <p>
          Album capsules must declare <code>album.world</code> in their manifest capabilities, and the
          CDN-issued creator key has to include it too. A creator trusted for a single-track capsule
          isn't automatically trusted for an album — each format's capabilities are intersected against
          the CDN key independently.
        </p>
      </>
    ),
    visual: {
      kind: 'code',
      filename: 'discography.spectral',
      code: `[4]  Magic .............. SPAC
[4]  Format version ..... 1
[32] Ed25519 public key
[64] Ed25519 signature
     ↓ ZIP payload
     manifest.json  (format: "spectralis-album")
     tracks/01-intro.flac
     tracks/02-title-card.flac
     world/index.html
     assets/images/cover.png`,
    },
  },
  {
    slug: 'reactive-timeline',
    icon: Zap,
    label: 'Sidecar format',
    ext: '.spectralis-reactive.json',
    title: 'The reactive timeline',
    summary: 'A JSON sidecar that drives theme, visualizer, lyrics, and shader changes synced frame-by-frame to playback position.',
    body: (
      <>
        <p>
          Any local file can carry a <code>.spectralis-reactive.json</code> sidecar — same base
          name, dropped right next to the audio, no re-encoding or tagging required. Inside a
          capsule the same document just lives at the ZIP root as <code>reactive.json</code>.
        </p>
        <p>
          Two structures do the work: named <strong>sections</strong> (non-overlapping time ranges
          with a mood label the OBS overlay can surface) and a <strong>timeline</strong> of events
          that fire at exact timestamps against one of four targets —
          <code>theme</code>, <code>visualizer</code>, <code>lyrics</code>, <code>shader</code>.
          Numeric parameters interpolate smoothly over a <code>duration</code> using one of six
          easing curves; everything else just snaps.
        </p>
        <p>
          The runtime is stateless between track loads and does a forward scan every tick — seek
          the timeline bar and it re-syncs instantly instead of replaying everything from zero.
        </p>
      </>
    ),
    visual: {
      kind: 'code',
      filename: 'track.spectralis-reactive.json',
      code: `"timeline": [
  {
    "time": 32.0,
    "target": "visualizer",
    "action": "set",
    "params": { "mode": "PulseRing" }
  },
  {
    "time": 64.0,
    "target": "theme",
    "action": "transition",
    "duration": 2.0,
    "easing": "incubic",
    "params": { "accent": "Violet" }
  }
]`,
    },
  },
  {
    slug: 'embedded-experiences',
    icon: FileCode2,
    label: 'Embedded content',
    ext: 'ID3v2 TXXX',
    title: 'Embedded experiences in plain MP3s',
    summary: 'WASM visualizers, HTML overlays, Markdown liner notes, and synced video — packed straight into ID3v2 tags, no capsule required.',
    body: (
      <>
        <p>
          Embedded modules let creators ship rich animated experiences directly inside plain MP3
          files as portable, self-contained content — no separate download, no capsule, it just rides
          along in the file. They live in ID3v2 <code>TXXX</code> frames and load into isolated
          runtime contexts inside Spectralis.
        </p>
        <p>
          Four module types cover most use cases: <code>visualizer</code> (a WASM binary),
          <code>html</code> (a rich overlay or fullscreen experience), <code>markdown</code>
          (formatted liner notes or lyrics), and <code>video</code> (H.264, VP9, AV1, or H.265,
          synced to playback). A module's definition frame points at binary and data frames by ID, so
          one track can bundle a visualizer plus its config and theme overrides without touching the
          audio stream itself.
        </p>
        <p>
          Binary payloads are base64-encoded inside their own <code>TXXX</code> frames, and config or
          theme data is capped at 64 KB per block — small enough to stay a normal MP3 anywhere else it
          gets played, large enough for a real visualizer.
        </p>
      </>
    ),
    visual: {
      kind: 'code',
      filename: 'ID3v2 frames',
      code: `TXXX:DELTA_MODULE_my_visualizer
  { "type": "visualizer", "runtime": "wasm",
    "entry": "_start", "binaryRef": "viz_wasm" }

TXXX:DELTA_BIN_viz_wasm
  <base64-encoded WASM binary>

TXXX:DELTA_DATA_config_json
  { "color": "#a882f2", "thickness": 2.5 }`,
    },
  },
  {
    slug: 'creator-tools',
    icon: NotebookPen,
    label: 'Creator workflow',
    ext: 'Ctrl+Shift+L',
    title: 'Built-in creator tools',
    summary: 'A Lyrics Timing Studio for tapping out .lrc files by ear, plus content warnings that gate playback behind a confirmation.',
    body: (
      <>
        <p>
          The <strong>Lyrics Timing Studio</strong> (<code>File → Lyrics Timing Studio…</code>, or
          <code>Ctrl+Shift+L</code>) is built for timing lyrics by ear without alt-tabbing to a
          separate app. Paste in plain lyric lines, tap each one to the current playback position as
          the track plays, nudge everything by 0.10s or 0.50s to fix drift, then export a matching
          <code>.lrc</code> sidecar right next to the audio file.
        </p>
        <p>
          Lyrics can also carry <strong>lyric explanations</strong> — Genius-style annotations that
          surface below the current line during playback. They're stored as timestamp-keyed JSON,
          either in a sidecar <code>.lrc.json</code> file or embedded directly in an ID3v2
          <code>TXXX</code> frame named <code>LYRIC_EXPLANATIONS</code>, so they travel with the file.
        </p>
        <p>
          <strong>Content warnings</strong> attach short labels — violence, flashing lights, loud
          sounds — to individual local tracks from the queue's right-click menu. Before a labeled
          track plays, a popup lists the tags and requires a "Play Anyway" click, whether playback was
          triggered manually or by queue auto-advance.
        </p>
      </>
    ),
    visual: null,
  },
  {
    slug: 'obs-overlay',
    icon: MonitorPlay,
    label: 'Streaming',
    ext: 'localhost:5128',
    title: 'The OBS overlay server',
    summary: 'A local web server with eleven layout presets, pushed live over Server-Sent Events — no polling, no page-reload flash between songs.',
    body: (
      <>
        <p>
          Spectralis runs a tiny local web server the moment it launches — a browser source
          pointed at <code>localhost:5128</code> gets a live, transparent overlay with zero
          setup. State pushes over Server-Sent Events the instant a track changes, so there's
          no polling interval to tune and no page-reload flash between songs.
        </p>
        <p>
          Eleven layout presets ship in the box — corner cards, a lyrics lower-third, a full-width
          spectrum banner, a queue sidebar — swapped with one query parameter, no OBS-side CSS
          editing required:
        </p>
      </>
    ),
    visual: {
      kind: 'code',
      filename: 'browser source url',
      code: `http://localhost:5128/overlay?preset=lyrics-lower-third
http://localhost:5128/overlay?preset=full-visualizer
http://localhost:5128/overlay?preset=queue-sidebar`,
    },
  },
  {
    slug: 'shared-play',
    icon: Users,
    label: 'Listen Together',
    ext: 'spectralis.app',
    title: 'Shared Play, room by room',
    summary: 'A short room code anyone can join from a browser — synced visualizer, live lyrics, reactions, and a listener-submitted queue.',
    body: (
      <>
        <p>
          Hosting hands out a short room code — <code>X7K-29Q</code> — that anyone can drop into
          <code>spectralis.app</code> from any browser, no account and no app install required
          to just listen along. The listener page shows a synced spinning-disc visualizer, a live
          spectrum meter, current + next lyric lines, and a running listener count with a
          connection-status dot.
        </p>
        <p>
          Rooms carry a floating reaction bar (❤️ 🔥 😮 ⚡ +1) and a collapsible queue where
          listeners can request a Spotify, YouTube, or direct audio link for the host to pick up.
          Discord Rich Presence publishes what's playing with a one-click "Listen Together" button
          straight from your status — the fastest path from "what's this song" to actually in the room.
        </p>
      </>
    ),
    visual: null,
  },
]

export function getArticle(slug) {
  return ARTICLES.find((a) => a.slug === slug)
}
