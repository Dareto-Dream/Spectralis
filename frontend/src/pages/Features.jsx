import { Shield, Zap } from 'lucide-react'
import { FEATURES, OBS_PRESETS, FORMAT_LIST } from '../data/site.jsx'
import { VisualizerGallery } from '../components/VisualizerGallery.jsx'

// ── full feature list ────────────────────────────────────────────────────────

function AllFeatures() {
  return (
    <section className="features section" id="features">
      <div className="section__head">
        <span className="section__label">Features</span>
        <h2 className="section__title">Everything you need.</h2>
      </div>
      <div className="feat-list">
        {FEATURES.map(({ num, icon: Icon, title, body }) => (
          <div key={num} className="feat-row">
            <span className="feat-row__num">{num}</span>
            <h3 className="feat-row__title">
              <Icon size={15} className="feat-row__icon" />
              {title}
            </h3>
            <p className="feat-row__body">{body}</p>
          </div>
        ))}
      </div>
    </section>
  )
}

// ── visualizers ────────────────────────────────────────────────────────────────

function Visualizers() {
  return (
    <section className="visualizers section" id="visualizers">
      <div className="section__head">
        <span className="section__label">Visual engine</span>
        <h2 className="section__title">15 visualizers, live.</h2>
      </div>
      <p className="viz-note">Real captures, straight from the app. Idle state.</p>
      <VisualizerGallery />
    </section>
  )
}

// ── OBS ────────────────────────────────────────────────────────────────────────

function OBSSection() {
  return (
    <section className="obs section" id="obs">
      <div className="section__head">
        <span className="section__label">OBS integration</span>
        <h2 className="section__title">Stream-ready,<br />out of the box.</h2>
      </div>
      <div className="obs-inner">
        <div className="obs-copy">
          <p className="obs-body">
            Spectralis serves a browser overlay at <code>localhost:5128</code> via SSE.
            Eleven layout presets via <code>?preset=</code>. State pushes the instant
            the track changes — no polling, no page reloads, no lag.
          </p>
          <ul className="obs-features">
            <li>High-DPI spectrum canvas</li>
            <li>Artwork cache-busting</li>
            <li>Current + next lyric lines</li>
            <li>Queue display</li>
            <li>SSE state push at 100 ms cadence</li>
          </ul>
        </div>
        <div className="obs-presets-grid">
          {OBS_PRESETS.map(({ id, label, desc, render }) => (
            <div key={id} className="obs-card">
              {render()}
              <div className="obs-card__info">
                <code className="obs-card__label">{label}</code>
                <p className="obs-card__desc">{desc}</p>
              </div>
            </div>
          ))}
        </div>
      </div>
    </section>
  )
}

// ── capsule ────────────────────────────────────────────────────────────────────

function CapsuleSection() {
  return (
    <section className="capsule section" id="capsule">
      <div className="section__head">
        <span className="section__label">Artist format</span>
        <h2 className="section__title">Signed. Sealed.<br />Self-contained.</h2>
      </div>
      <div className="cap-inner">
        <div className="cap-left">
          <p className="cap-body">
            A <code>.spectralis</code> file is a binary capsule — Ed25519-signed,
            self-verifying, self-contained. Audio, art, lyrics, reactive timeline
            events, and sandboxed WASM visualizers live in one file that creators
            sign and listeners trust.
          </p>
          <div className="cap-card">
            <div className="cap-card__bar">
              <span className="cap-card__dot" style={{ background: '#ef4444' }} />
              <span className="cap-card__dot" style={{ background: '#eab308' }} />
              <span className="cap-card__dot" style={{ background: '#22c55e' }} />
              <code className="cap-card__name">release.spectralis</code>
            </div>
            <pre className="cap-card__code">{`[4]  Magic .............. SPCC
[4]  Format version ..... 3
[32] Ed25519 public key
[64] Ed25519 signature
     ↓ ZIP payload
     manifest.json
     audio/track.flac
     reactive.json
     assets/images/cover.png
     assets/data/lyrics.lrc`}</pre>
          </div>
        </div>
        <div className="cap-right">
          <div className="cap-formats">
            <p className="cap-formats__label">Supported formats</p>
            <div className="cap-formats__list">
              {FORMAT_LIST.map(f => <span key={f} className="cap-formats__pill">{f}</span>)}
            </div>
          </div>
          <div className="cap-trust">
            <Shield size={15} className="cap-trust__icon" />
            <div>
              <p className="cap-trust__title">Creator trust system</p>
              <p className="cap-trust__body">Unknown keys trigger a one-time trust prompt. Revoked keys always rejected. Capabilities intersection-enforced against cdn.deltavdevs.com.</p>
            </div>
          </div>
          <div className="cap-trust">
            <Zap size={15} className="cap-trust__icon" />
            <div>
              <p className="cap-trust__title">Reactive timeline</p>
              <p className="cap-trust__body">Section tracking, smooth parameter transitions, and easing curves synchronized frame-by-frame to playback position.</p>
            </div>
          </div>
        </div>
      </div>
    </section>
  )
}

export default function Features() {
  return (
    <>
      <div className="page-head">
        <h1 className="page-head__title">One app,<br />every angle of the music.</h1>
      </div>
      <AllFeatures />
      <Visualizers />
      <OBSSection />
      <CapsuleSection />
    </>
  )
}
