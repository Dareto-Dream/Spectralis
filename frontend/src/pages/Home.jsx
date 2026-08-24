import { useState, useEffect } from 'react'
import { Link } from 'react-router-dom'
import { Download, Terminal, Apple, ArrowRight, GitFork, Sparkles } from 'lucide-react'
import { SCREENSHOTS, CHANGELOG_URL, CHANGELOG_ICONS } from '../data/site.jsx'
import { CommunityBar } from '../components/CommunityBar.jsx'
import { VisualizerStackGrid } from '../components/VisualizerStackGrid.jsx'

// ── hero (+ video) ───────────────────────────────────────────────────────────

function Hero() {
  return (
    <section className="hero" id="hero">
      <div className="hero__bg" aria-hidden="true" />
      <div className="hero__center">
        <img src="/icon.png" alt="Spectralis" className="hero__logo" />
        <h1 className="hero__title">Spectralis</h1>
        <p className="hero__sub">
          A desktop audio player that renders real-time visualizers, syncs lyrics
          as they play, runs its own OBS overlay server, and reads signed capsule
          releases from artists shipping more than an MP3.
        </p>
        <div className="hero__actions">
          {/* TODO: point at slugged update pages (e.g. /updates/:slug) once they exist, instead of #changelog */}
          <a href="#changelog" className="btn btn--ghost btn--lg hero__whats-new">
            What's new
          </a>
          <Link to="/setup" className="btn btn--primary btn--lg">
            Download Spectralis
            <Download size={15} />
          </Link>
        </div>
      </div>
      <div className="hero__preview">
        <video
          className="hero__preview-video"
          src="/hero-preview.mp4"
          poster="/hero-preview-poster.jpg"
          autoPlay
          loop
          muted
          playsInline
        />
      </div>
      <p className="hero__community-label">Some of the listeners and creators who use Spectralis</p>
      <CommunityBar />
    </section>
  )
}

// ── downloads ────────────────────────────────────────────────────────────────

const DOWNLOAD_PITCH = [
  {
    title: 'Start listening today',
    body: 'Point it at your library and go. Visualizers, lyrics sync, and capsule playback are already switched on the first time you open the app.',
  },
  {
    title: 'As deep as your library',
    body: 'The same install includes all 15 built-in visualizer renderers, the OBS overlay server, and signed capsule support. None of it ships as a separate plugin or download.',
  },
  {
    title: 'Free updates, for life',
    body: 'Every update and every new feature ships free, forever, with no account, purchase, or key standing in the way.',
  },
]

function DownloadsSection() {
  return (
    <section className="section" id="downloads">
      <div className="dl-split">
        <div className="dl-split__pitch">
          <span className="section__label">Get it</span>
          <h2 className="section__title">Download free.</h2>
          <div className="dl-pitch">
            {DOWNLOAD_PITCH.map(({ title, body }) => (
              <div className="dl-pitch__item" key={title}>
                <h3 className="dl-pitch__title">{title}</h3>
                <p className="dl-pitch__body">{body}</p>
              </div>
            ))}
          </div>
        </div>

        <div className="dl-split__os">
          <h3 className="dl-split__os-title">Pick your OS</h3>
          <div className="dl-os-list">
            <Link to="/setup#windows" className="dl-teaser__card">
              <span className="dl-teaser__icon"><Download size={20} /></span>
              <span className="dl-teaser__text">
                <span className="dl-teaser__name">Windows</span>
                <span className="dl-teaser__sub">10 / 11 · x64 installer</span>
              </span>
              <ArrowRight size={16} className="dl-teaser__arrow" />
            </Link>
            <div className="dl-teaser__card dl-teaser__card--soon">
              <span className="dl-teaser__icon"><Apple size={20} /></span>
              <span className="dl-teaser__text">
                <span className="dl-teaser__name">macOS</span>
                <span className="dl-teaser__sub">Universal · Apple Silicon &amp; Intel</span>
              </span>
              <span className="dl-teaser__badge">Coming soon</span>
            </div>
            <Link to="/setup#linux" className="dl-teaser__card">
              <span className="dl-teaser__icon"><Terminal size={20} /></span>
              <span className="dl-teaser__text">
                <span className="dl-teaser__name">Linux</span>
                <span className="dl-teaser__sub">x86_64 · AppImage</span>
              </span>
              <ArrowRight size={16} className="dl-teaser__arrow" />
            </Link>
          </div>
        </div>
      </div>
    </section>
  )
}

// ── see what's inside ────────────────────────────────────────────────────────

function InsideSection() {
  const [{ src, title }] = SCREENSHOTS
  return (
    <section className="shots section" id="screenshots">
      <div className="shots__box">
        <div className="section__head">
          <span className="section__label">In the app</span>
          <h2 className="section__title">See what's inside.</h2>
          <p className="section__sub">Visualizers, synced lyrics, capsule releases, and streamer tools all ship in the same free app, with more features already in progress.</p>
        </div>
        <Link to="/features" className="btn btn--ghost section__cta shots__cta">
          All Features
          <ArrowRight size={14} />
        </Link>
        <figure className="shots__shot">
          <img src={src} alt={title} loading="lazy" />
        </figure>
      </div>
    </section>
  )
}

// ── make great visualizers ("behind the hits" equivalent) ───────────────────

function VisualizersSection() {
  return (
    <section className="visualizers section" id="visualizers">
      <div className="section__head">
        <span className="section__label">Visual engine</span>
        <h2 className="section__title">Built-in visualizers</h2>
        <p className="section__sub">Spectrum bars, oscilloscopes, a spinning album disc, and a 3D sphere, ready to switch between mid-song.</p>
      </div>
      <VisualizerStackGrid />
    </section>
  )
}

// ── what's new (changelog) ───────────────────────────────────────────────────

function ChangelogSection() {
  const [releases, setReleases] = useState(null)
  const [loadError, setLoadError] = useState(false)
  const [activeVersion, setActiveVersion] = useState(null)

  useEffect(() => {
    let cancelled = false
    fetch(CHANGELOG_URL)
      .then(res => { if (!res.ok) throw new Error(res.status); return res.json() })
      .then(data => {
        if (cancelled) return
        setReleases(data)
        setActiveVersion(data[0]?.version ?? null)
      })
      .catch(() => { if (!cancelled) setLoadError(true) })
    return () => { cancelled = true }
  }, [])

  const activeRelease = releases?.find(r => r.version === activeVersion) ?? releases?.[0]
  const isLatest = activeRelease && releases && activeRelease.version === releases[0].version

  return (
    <section className="changelog section" id="changelog">
      <div className="section__head">
        <span className="section__label">Changelog</span>
        <h2 className="section__title">What's new.</h2>
        <p className="section__sub">Every release, from patch notes to major features.</p>
      </div>

      {loadError && (
        <p className="changelog-status">
          Couldn't reach the changelog feed. <a href="https://github.com/dareto-dream/spectralis/releases" target="_blank" rel="noreferrer">See releases on GitHub</a> instead.
        </p>
      )}

      {!loadError && !releases && (
        <p className="changelog-status">Loading changelog…</p>
      )}

      {activeRelease && (
        <div className="changelog-shell">
          <nav className="changelog-rail" aria-label="Release versions">
            <div className="changelog-rail__line" />
            {releases.map((release, i) => (
              <button
                key={release.version}
                type="button"
                className={`changelog-rail__item${release.version === activeVersion ? ' active' : ''}`}
                onClick={() => setActiveVersion(release.version)}
              >
                <span className="changelog-rail__dot" />
                <span className="changelog-rail__version">
                  {release.version}
                  {i === 0 && <span className="changelog-rail__badge">NEW</span>}
                </span>
                <span className="changelog-rail__label">{release.label}</span>
              </button>
            ))}
          </nav>

          <article className="changelog-release">
            {isLatest && (
              <div className="changelog-release__latest-bar">
                <span className="changelog-release__latest-dot" />
                Latest release
              </div>
            )}

            <div className="changelog-release__top">
              <div>
                <p className="changelog-release__eyebrow">{activeRelease.date}</p>
                <h3 className="changelog-release__title">{activeRelease.version}</h3>
              </div>
              <div className="changelog-release__metrics">
                {activeRelease.metrics.map(metric => (
                  <span key={metric}>{metric}</span>
                ))}
              </div>
            </div>

            <p className="changelog-release__summary">{activeRelease.summary}</p>

            <div className="changelog-features">
              {activeRelease.groups.map(({ icon, title, bullets }) => {
                const Icon = CHANGELOG_ICONS[icon] ?? Sparkles
                return (
                  <section key={title} className="changelog-feature">
                    <div className="changelog-feature__head">
                      <span className="changelog-feature__icon"><Icon size={16} /></span>
                      <h4>{title}</h4>
                    </div>
                    <ul>
                      {bullets.map(item => <li key={item}>{item}</li>)}
                    </ul>
                  </section>
                )
              })}
            </div>
          </article>
        </div>
      )}
    </section>
  )
}

// ── start listening ──────────────────────────────────────────────────────────

function CTASection() {
  return (
    <section className="cta section" id="download">
      <div className="cta-inner">
        <img src="/icon.png" alt="Spectralis" className="cta-logo" />
        <div className="cta-text">
          <h2 className="cta-title">Start listening.</h2>
          <p className="cta-sub">Windows 10/11 · Linux x86_64 · No sign-in · Free</p>
          <div className="cta-actions">
            <Link to="/setup" className="btn btn--primary btn--lg">
              <Download size={16} />
              Get Spectralis
            </Link>
            <a href="https://github.com/dareto-dream/spectralis" target="_blank" rel="noreferrer" className="btn btn--ghost btn--lg">
              <GitFork size={16} />
              View on GitHub
            </a>
          </div>
        </div>
      </div>
    </section>
  )
}

export default function Home() {
  return (
    <>
      <Hero />
      <DownloadsSection />
      <InsideSection />
      <VisualizersSection />
      <ChangelogSection />
      <CTASection />
    </>
  )
}
