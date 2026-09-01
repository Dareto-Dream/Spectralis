import { useState, useLayoutEffect } from 'react'
import { Link } from 'react-router-dom'
import { Apple, ArrowRight, X } from 'lucide-react'

// bump the suffix when the announcement changes so a fresh message re-shows
const STORAGE_KEY = 'spectralis-banner-mac-v6'

export function AnnouncementBanner() {
  const [dismissed, setDismissed] = useState(() => {
    try { return localStorage.getItem(STORAGE_KEY) === '1' } catch { return false }
  })

  // the banner is fixed; --banner-h offsets the navbar and page content beneath it
  useLayoutEffect(() => {
    document.documentElement.style.setProperty('--banner-h', dismissed ? '0px' : '40px')
  }, [dismissed])

  if (dismissed) return null

  const dismiss = () => {
    try { localStorage.setItem(STORAGE_KEY, '1') } catch { /* private mode */ }
    setDismissed(true)
  }

  return (
    <div className="promo-banner">
      <Link to="/setup#macos" className="promo-banner__msg">
        <Apple size={13} />
        <span className="promo-banner__text">Spectralis for Mac is here — Apple Silicon &amp; Intel builds are live.</span>
        <span className="promo-banner__cta">Get it <ArrowRight size={12} /></span>
      </Link>
      <button
        type="button"
        className="promo-banner__close"
        onClick={dismiss}
        aria-label="Dismiss announcement"
      >
        <X size={13} />
      </button>
    </div>
  )
}
