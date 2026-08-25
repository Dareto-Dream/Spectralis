import { Link } from 'react-router-dom'
import { Download, Terminal, Image, AudioWaveform } from 'lucide-react'

const WIDGET_PLACEHOLDERS = [
  { title: 'Easy to start listening', body: 'Open your library and press play. The interface stays out of the way between tracks.', image: '/screenshots/start-somewhere.png' },
  { title: 'Fun for everyone', body: 'Queue voting, song wars, and randomizer tools built for streamers running a live broadcast.', image: '/screenshots/fun-for-everyone.png' },
  { title: 'Tools for creators', body: 'Build custom visualizer configs and ship them straight into a capsule release.', image: '/screenshots/tooling-for-creators.png' },
]

const WIDGET_PLACEHOLDERS_LG = [
  { title: 'Capsule releases', body: 'Each capsule bundles a track with signed art, lyrics, and a reactive visual timeline.', image: '/screenshots/capsules.png' },
  { title: 'Album worlds', body: 'Ship a whole album as an interactive HTML world that creators build and control end to end.', image: '/screenshots/album-worlds.png' },
]

const GRID_PLACEHOLDERS = [
  { title: 'Mirror Spectrum', subtitle: 'A mirrored spectrum analyzer that reacts in real time.', image: '/screenshots/visualizers/mirror-spectrum.jpg' },
  { title: 'Spinning Disk', subtitle: 'Bring back retro with our spinning disk visualizer.', image: '/screenshots/visualizers/album-cover.jpg' },
  { title: 'Waveform', subtitle: 'Visualize your audio with our waveform display.', image: '/screenshots/visualizers/waveform.jpg' },
  { title: 'Spectrogram', subtitle: 'Analyze your favorites with our spectrogram display.', image: '/screenshots/visualizers/spectrogram.jpg' },
  { title: 'Dancing Colors', subtitle: 'Color fields that shift and pulse with the track.', image: '/screenshots/visualizers/dancing-colors.jpg' },
  { title: '3D Graph', subtitle: 'Bring your music into the 3rd dimension', image: '/screenshots/visualizers/3d-graph.jpg' },
]

// ── full feature list ────────────────────────────────────────────────────────

function AllFeatures() {
  return (
    <section className="features section feat-masthead" id="features">
      <h1 className="feat-masthead__title">Features</h1>
      <p className="feat-masthead__tagline">Everything Spectralis does, gathered on one page.</p>
      <Link to="/setup" className="btn btn--primary btn--lg feat-masthead__cta">
        Get Spectralis
        <Download size={15} />
      </Link>
    </section>
  )
}

// ── visualizers ────────────────────────────────────────────────────────────────

function Visualizers() {
  return (
    <section className="visualizers section" id="visualizers">
      <div className="feat-preview-box">
        <div className="viz-preview-head">
          <div>
            <h2 className="viz-preview-head__title">Visualizers, built in</h2>
            <p className="viz-preview-head__desc">Real-time visualizers render every track automatically, switchable without leaving the now-playing screen.</p>
          </div>
          <span className="viz-preview-head__os">
            <Download size={14} /> Windows &nbsp;·&nbsp; <Terminal size={14} /> Linux
          </span>
        </div>
        <div className="feat-preview">
          <video
            className="feat-preview-video"
            src="/features-preview.mp4"
            poster="/features-preview-poster.jpg"
            autoPlay
            loop
            muted
            playsInline
          />
        </div>
      </div>
      <div className="feat-widgets">
        {WIDGET_PLACEHOLDERS.map(({ title, body, image }) => (
          <div className="feat-widget" key={title}>
            <h3 className="feat-widget__title">{title}</h3>
            <p className="feat-widget__body">{body}</p>
            <div className="feat-widget__image">
              {image ? <img src={image} alt="" /> : <Image size={28} />}
            </div>
          </div>
        ))}
      </div>
    </section>
  )
}

// ── visualizers cta ──────────────────────────────────────────────────────────

function VisualizersCTA() {
  return (
    <section className="viz-cta section" id="capsules-worlds">
      <div className="viz-cta__head">
        <span className="viz-cta__icon"><AudioWaveform size={22} /></span>
        <div>
          <h2 className="viz-cta__title">Capsule releases and album worlds</h2>
          <p className="viz-cta__desc">Two ways artists can ship more than an MP3, plus every visualizer built into the app.</p>
        </div>
      </div>
      <div className="feat-widgets-lg">
        {WIDGET_PLACEHOLDERS_LG.map(({ title, body, image }) => (
          <div className="feat-widget-lg" key={title}>
            <h3 className="feat-widget-lg__title">{title}</h3>
            <p className="feat-widget-lg__body">{body}</p>
            <div className="feat-widget-lg__image">
              {image ? <img src={image} alt="" /> : <Image size={36} />}
            </div>
          </div>
        ))}
      </div>
      <div className="feat-grid">
        {GRID_PLACEHOLDERS.map(({ title, subtitle, image }) => (
          <div className="feat-grid__item" key={title}>
            <div className="feat-grid__image">
              {image ? <img src={image} alt="" /> : <Image size={26} />}
            </div>
            <h3 className="feat-grid__title">{title}</h3>
            <p className="feat-grid__subtitle">{subtitle}</p>
          </div>
        ))}
      </div>
    </section>
  )
}

// ── get spectralis ───────────────────────────────────────────────────────────

function GetSpectralisCTA() {
  return (
    <section className="feat-banner section" id="get-spectralis">
      <div className="feat-banner__inner">
        <div>
          <h2 className="feat-banner__title">That's the whole feature set.</h2>
          <p className="feat-banner__sub">Free on Windows and Linux, no account required.</p>
        </div>
        <div className="feat-banner__actions">
          <a href="/#changelog" className="btn btn--ghost btn--lg">What's new</a>
          <Link to="/setup" className="btn btn--primary btn--lg">
            Get Spectralis
            <Download size={16} />
          </Link>
        </div>
      </div>
    </section>
  )
}

export default function Features() {
  return (
    <>
      <AllFeatures />
      <Visualizers />
      <VisualizersCTA />
      <GetSpectralisCTA />
    </>
  )
}
