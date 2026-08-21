import { Package, Zap, MonitorPlay, Users } from 'lucide-react'

const ENTRIES = [
  {
    id: 'capsules',
    icon: Package,
    label: 'Artist format',
    title: 'Inside a .spectralis capsule',
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
    id: 'reactive-timeline',
    icon: Zap,
    label: 'Sidecar format',
    title: 'The reactive timeline',
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
    id: 'obs-overlay',
    icon: MonitorPlay,
    label: 'Streaming',
    title: 'The OBS overlay server',
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
    id: 'shared-play',
    icon: Users,
    label: 'Listen Together',
    title: 'Shared Play, room by room',
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

function LearnEntry({ entry, reversed }) {
  const Icon = entry.icon
  return (
    <section className={`learn-entry section${reversed ? ' learn-entry--reverse' : ''}`} id={entry.id}>
      <div className="learn-entry__copy">
        <div className="section__head">
          <span className="section__label">{entry.label}</span>
          <h2 className="section__title">
            <Icon size={22} className="learn-entry__icon" />
            {entry.title}
          </h2>
        </div>
        <div className="learn-entry__body">{entry.body}</div>
      </div>
      {entry.visual && (
        <div className="learn-entry__visual">
          <div className="cap-card">
            <div className="cap-card__bar">
              <span className="cap-card__dot" style={{ background: '#ef4444' }} />
              <span className="cap-card__dot" style={{ background: '#eab308' }} />
              <span className="cap-card__dot" style={{ background: '#22c55e' }} />
              <code className="cap-card__name">{entry.visual.filename}</code>
            </div>
            <pre className="cap-card__code">{entry.visual.code}</pre>
          </div>
        </div>
      )}
    </section>
  )
}

export default function Learn() {
  return (
    <>
      <div className="page-head">
        <span className="section__label">Learn</span>
        <h1 className="page-head__title">How Spectralis<br />actually works.</h1>
      </div>
      {ENTRIES.map((entry, i) => (
        <LearnEntry key={entry.id} entry={entry} reversed={i % 2 === 1} />
      ))}
    </>
  )
}
