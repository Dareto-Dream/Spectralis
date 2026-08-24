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
    <section className="features section" id="features">
      <div className="page-head">
        <h1 className="page-head__title">Features</h1>
      </div>
      <div className="section__head">
        <h2 className="section__sub">From energetic mornings to relaxing evenings<br/>Spectralis has got your back</h2>
      </div>
      <Link to="/setup" className="btn btn--primary btn--lg feat-list__cta">
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
        <div className="section__head">
          <h2 className="section__title">Visualizers, built in</h2>
          <h2 className="section__sub">Real-time visualizers render every track automatically, switchable without leaving the now-playing screen</h2>
        </div>
        <p className="viz-availability">
          Available on <Download size={15} className="viz-availability__icon" /> Windows and <Terminal size={15} className="viz-availability__icon" /> Linux
        </p>
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
    <section className="viz-cta section" id="obs">
      <div className="section__head">
        <div className="viz-cta__icon">
          <AudioWaveform size={28} />
        </div>
        <h2 className="section__title">Visualizers to encourage</h2>
        <p className="section__sub">Get Spectralis' wide range of visualizers and tools, from built-in waveforms to deep visual design tools.</p>
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
    <section className="cta section" id="capsule">
      <div className="cta-inner">
        <img src="/icon.png" alt="Spectralis" className="cta-logo" />
        <div className="cta-text">
          <h2 className="cta-title">Get Spectralis today</h2>
          <p className="cta-sub">See if it meets your needs</p>
          <div className="cta-actions">
            <a href="/#changelog" className="btn btn--ghost btn--lg">What's new</a>
            <Link to="/setup" className="btn btn--primary btn--lg">
              Get Spectralis
              <Download size={16} />
            </Link>
          </div>
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
