import { Link } from 'react-router-dom'
import { Download, Terminal, GitFork, ArrowUpRight } from 'lucide-react'

const FAQ = [
  {
    q: 'Is Spectralis really free?',
    a: 'Yes, completely. Nothing is gated behind a purchase, and every release ships free, forever.',
  },
  {
    q: 'Do I need to sign in or create an account?',
    a: 'No. Spectralis runs fully offline out of the box. Signing in only matters if you connect Spotify or Discord.',
  },
  {
    q: 'Will installing overwrite my existing library or settings?',
    a: 'No. Windows installs to %LocalAppData%\\Spectralis, and Linux runs as a self-contained AppImage; neither touches files outside its own folder.',
  },
  {
    q: 'How do updates work?',
    a: 'Windows uses Velopack: it checks on launch and applies delta patches silently, no re-installer needed. On Linux, just download the latest AppImage when a new version ships.',
  },
  {
    q: 'Is macOS supported?',
    a: 'Not yet, though it\'s on the roadmap. Windows and Linux are supported today.',
  },
  {
    q: 'The AppImage won\'t launch on Linux. What do I do?',
    a: 'Most distros ship FUSE by default. If yours doesn\'t, run the AppImage with --appimage-extract-and-run as a fallback.',
  },
]

export default function Setup() {
  return (
    <main className="dl-page">
      <div className="dl-hero">
        <div className="dl-hero__inner">
          <div className="dl-hero__head">
            <div className="dl-hero__version-row">
              <span className="dl-hero__v">v5.2.0</span>
              <span className="dl-hero__v-badge">Latest</span>
            </div>
            <h1 className="dl-hero__title">Setup & Requirements</h1>
            <p className="dl-hero__sub">Free · No sign-in required · Self-contained</p>
          </div>
          <div className="dl-hero__meta">
            <div className="dl-hero__stat">
              <span className="dl-hero__stat-n">Windows</span>
              <span className="dl-hero__stat-l">Velopack installer</span>
            </div>
            <div className="dl-hero__stat">
              <span className="dl-hero__stat-n">Linux</span>
              <span className="dl-hero__stat-l">AppImage · New in v5</span>
            </div>
            <div className="dl-hero__stat">
              <span className="dl-hero__stat-n">Free</span>
              <span className="dl-hero__stat-l">No license key</span>
            </div>
          </div>
        </div>
      </div>

      <div className="dl-platforms">

        {/* Windows */}
        <div className="dl-card dl-card--windows" id="windows">
          <div className="dl-card__header">
            <div className="dl-card__icon">
              <Download size={18} />
            </div>
            <div className="dl-card__header-text">
              <h2 className="dl-card__title">Windows</h2>
              <p className="dl-card__platform-sub">10 / 11 · x64</p>
            </div>
            <span className="dl-card__badge">Recommended</span>
          </div>

          <div className="dl-card__file">
            <span className="dl-card__filename">Spectralis-win-x64-Setup.exe</span>
            <span className="dl-card__filetype">Velopack installer</span>
          </div>

          <a
            href="https://cdn.deltavdevs.com/spectralis/Spectralis-win-x64-Setup.exe"
            className="btn btn--primary btn--lg dl-card__btn"
          >
            <Download size={16} />
            Download for Windows
          </a>

          <div className="dl-card__notes">
            <div className="dl-note">
              <span className="dl-note__label">Auto-updates</span>
              <p className="dl-note__body">Velopack checks for updates on launch and applies delta patches silently, so there's no re-installer to run.</p>
            </div>
            <div className="dl-note">
              <span className="dl-note__label">Install path</span>
              <p className="dl-note__body">Installs to <code>%LocalAppData%\Spectralis</code> and doesn't need admin rights.</p>
            </div>
          </div>

          <div className="dl-card__steps">
            <p className="dl-steps__label">To install</p>
            <ol className="dl-steps">
              <li>Run <code>Setup.exe</code></li>
              <li>Spectralis launches automatically after install</li>
              <li>Future updates apply on next launch</li>
            </ol>
          </div>
        </div>

        {/* Linux */}
        <div className="dl-card dl-card--linux" id="linux">
          <div className="dl-card__header">
            <div className="dl-card__icon">
              <Terminal size={18} />
            </div>
            <div className="dl-card__header-text">
              <h2 className="dl-card__title">Linux</h2>
              <p className="dl-card__platform-sub">x86_64 · AppImage</p>
            </div>
            <span className="dl-card__badge dl-card__badge--new">New in v5</span>
          </div>

          <div className="dl-card__file">
            <span className="dl-card__filename">Spectralis-linux-x64.AppImage</span>
            <span className="dl-card__filetype">Self-contained · No install required</span>
          </div>

          <a
            href="https://cdn.deltavdevs.com/spectralis/Spectralis-linux-x64.AppImage"
            className="btn btn--primary btn--lg dl-card__btn"
          >
            <Download size={16} />
            Download AppImage
          </a>

          <div className="dl-card__notes">
            <div className="dl-note">
              <span className="dl-note__label">Self-contained</span>
              <p className="dl-note__body">Everything it needs is bundled in, so it runs without installing dependencies or needing root access.</p>
            </div>
            <div className="dl-note">
              <span className="dl-note__label">FUSE</span>
              <p className="dl-note__body">Most distros include FUSE. If not, run with <code>--appimage-extract-and-run</code> as a fallback.</p>
            </div>
          </div>

          <div className="dl-card__steps">
            <p className="dl-steps__label">To run</p>
            <ol className="dl-steps">
              <li><code>chmod +x Spectralis-linux-x64.AppImage</code></li>
              <li>Double-click or run from terminal</li>
              <li>Optional: use AppImageLauncher for system integration</li>
            </ol>
          </div>
        </div>
      </div>

      <div className="dl-requirements" id="requirements">
        <h2 className="dl-requirements__title">System requirements</h2>
        <div className="dl-req-grid">
          <div className="dl-req-col">
            <h3>Windows</h3>
            <table className="dl-req-table">
              <tbody>
                <tr><td>OS</td><td>Windows 10 or 11 (64-bit)</td></tr>
                <tr><td>Runtime</td><td>.NET 8 (bundled)</td></tr>
                <tr><td>Display</td><td>WebView2 (bundled)</td></tr>
                <tr><td>Arch</td><td>x64</td></tr>
              </tbody>
            </table>
          </div>
          <div className="dl-req-col">
            <h3>Linux</h3>
            <table className="dl-req-table">
              <tbody>
                <tr><td>OS</td><td>Any modern x86_64 Linux distro</td></tr>
                <tr><td>libc</td><td>glibc 2.17 or newer</td></tr>
                <tr><td>FUSE</td><td>libfuse2 (optional)</td></tr>
                <tr><td>Arch</td><td>x86_64</td></tr>
              </tbody>
            </table>
          </div>
        </div>
      </div>

      <div className="dl-faq" id="faq">
        <h2 className="dl-requirements__title">Frequently asked questions</h2>
        <div className="dl-faq__list">
          {FAQ.map(({ q, a }) => (
            <details className="dl-faq__item" key={q}>
              <summary className="dl-faq__q">{q}</summary>
              <p className="dl-faq__a">{a}</p>
            </details>
          ))}
        </div>
      </div>

      <div className="dl-footer-links">
        <Link to="/" className="btn btn--ghost">
          ← Back to home
        </Link>
        <a
          href="https://github.com/dareto-dream/spectralis"
          target="_blank"
          rel="noreferrer"
          className="btn btn--ghost"
        >
          <GitFork size={14} />
          View source on GitHub
          <ArrowUpRight size={13} />
        </a>
      </div>
    </main>
  )
}
