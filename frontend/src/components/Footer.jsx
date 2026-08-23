import { Link } from 'react-router-dom'

export function Footer() {
  return (
    <footer className="footer">
      <div className="footer__inner">
        <div className="footer__brand">
          <img src="/icon.png" alt="Spectralis" className="footer__logo" />
          <div>
            <p className="footer__name">Spectralis</p>
            <p className="footer__sub">Audio player, visualizer, and OBS overlay server.</p>
          </div>
        </div>
        <div className="footer__mid">
          <p className="footer__by">
            Made by{' '}
            <a href="https://deltavdevs.com" target="_blank" rel="noreferrer" className="footer__dvlink">
              DeltaV Devs
            </a>
          </p>
          <p className="footer__deltawave">
            From the creators of{' '}
            <a href="https://deltavdevs.com" target="_blank" rel="noreferrer" className="footer__dvlink">
              DeltaWave
            </a>
          </p>
        </div>
        <nav className="footer__nav">
          <Link to="/features">Features</Link>
          <Link to="/learn">Learn</Link>
          <Link to="/setup">Setup & Requirements</Link>
          <a href="https://github.com/dareto-dream/spectralis" target="_blank" rel="noreferrer">GitHub ↗</a>
        </nav>
        <div className="footer__bottom">
          <p className="footer__copy">© 2025 DeltaV Devs</p>
          <nav className="footer__legal">
            <Link to="/terms">Terms of Service</Link>
            <Link to="/privacy">Privacy Policy</Link>
          </nav>
        </div>
      </div>
    </footer>
  )
}
