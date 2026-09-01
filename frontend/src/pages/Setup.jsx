import { Link } from 'react-router-dom'
import { Download, Terminal, Apple, GitFork, ArrowUpRight } from 'lucide-react'

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
    a: 'No. Windows installs to %LocalAppData%\\Spectralis, macOS installs Spectralis.app to /Applications, and Linux runs as a self-contained AppImage; none of them touch files outside their own location.',
  },
  {
    q: 'How do updates work?',
    a: 'Every platform uses Velopack. Spectralis checks for updates on launch and applies delta patches in place, so there is no re-installer to run on Windows, macOS, or Linux.',
  },
  {
    q: 'Is macOS supported?',
    a: 'Yes, as of v6. There are separate builds for Apple Silicon (arm64) and Intel (x64) Macs running macOS 11 or newer.',
  },
  {
    q: 'macOS won\'t open the installer. What do I do?',
    a: 'If Gatekeeper blocks it, right-click (or Control-click) the .pkg and choose Open, or allow it under System Settings → Privacy & Security, then run it again.',
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
              <span className="dl-hero__v">v6.0.0</span>
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
              <span className="dl-hero__stat-n">macOS</span>
              <span className="dl-hero__stat-l">.pkg · New in v6</span>
            </div>
            <div className="dl-hero__stat">
              <span className="dl-hero__stat-n">Linux</span>
              <span className="dl-hero__stat-l">AppImage · x86_64</span>
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

        {/* macOS */}
        <div className="dl-card dl-card--macos" id="macos">
          <div className="dl-card__header">
            <div className="dl-card__icon">
              <Apple size={18} />
            </div>
            <div className="dl-card__header-text">
              <h2 className="dl-card__title">macOS</h2>
              <p className="dl-card__platform-sub">11 Big Sur or newer</p>
            </div>
            <span className="dl-card__badge dl-card__badge--new">New in v6</span>
          </div>

          <div className="dl-card__file">
            <span className="dl-card__filename">Spectralis-osx-&lt;arch&gt;-Setup.pkg</span>
            <span className="dl-card__filetype">.pkg installer · pick your chip</span>
          </div>

          <div className="dl-card__arch">
            <a
              href="https://cdn.deltavdevs.com/spectralis/Spectralis-osx-arm64-Setup.pkg"
              className="btn btn--primary dl-card__arch-btn"
            >
              <Download size={16} />
              Apple Silicon
              <span className="dl-card__arch-tag">arm64</span>
            </a>
            <a
              href="https://cdn.deltavdevs.com/spectralis/Spectralis-osx-x64-Setup.pkg"
              className="btn btn--ghost dl-card__arch-btn"
            >
              <Download size={16} />
              Intel
              <span className="dl-card__arch-tag">x64</span>
            </a>
          </div>

          <div className="dl-card__notes">
            <div className="dl-note">
              <span className="dl-note__label">Which one?</span>
              <p className="dl-note__body">M1 through M4 Macs take the Apple Silicon build; 2020-and-earlier Intel Macs take the Intel build. Not sure? Check <code>Apple menu → About This Mac</code>.</p>
            </div>
            <div className="dl-note">
              <span className="dl-note__label">Auto-updates</span>
              <p className="dl-note__body">Like Windows, macOS runs on Velopack and applies delta patches on launch, so there's no re-installer to chase.</p>
            </div>
            <div className="dl-note">
              <span className="dl-note__label">Gatekeeper</span>
              <p className="dl-note__body">If macOS won't open the <code>.pkg</code>, Control-click it and choose Open, or allow it under <code>System Settings → Privacy &amp; Security</code>.</p>
            </div>
          </div>

          <div className="dl-card__steps">
            <p className="dl-steps__label">To install</p>
            <ol className="dl-steps">
              <li>Open the <code>.pkg</code> and follow the installer</li>
              <li>Spectralis installs to <code>/Applications</code></li>
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
              <span className="dl-note__label">Auto-updates</span>
              <p className="dl-note__body">Velopack checks on launch and applies delta patches in place, same as Windows and macOS — no need to re-download the AppImage.</p>
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
            <h3>macOS</h3>
            <table className="dl-req-table">
              <tbody>
                <tr><td>OS</td><td>macOS 11 Big Sur or newer</td></tr>
                <tr><td>Runtime</td><td>.NET 8 (bundled)</td></tr>
                <tr><td>Chip</td><td>Apple Silicon or Intel</td></tr>
                <tr><td>Arch</td><td>arm64 · x64</td></tr>
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
