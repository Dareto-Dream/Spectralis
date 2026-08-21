import { useState, useEffect } from 'react'
import { NavLink, Link } from 'react-router-dom'
import { Download, GitFork, ArrowUpRight } from 'lucide-react'

const NAV_ITEMS = [
  {
    label: 'Features',
    to: '/features',
    menu: [
      { label: 'All Features', to: '/features#features' },
      { label: 'Visualizers', to: '/features#visualizers' },
      { label: 'OBS Overlay', to: '/features#obs' },
      { label: 'Capsule Format', to: '/features#capsule' },
    ],
  },
  {
    label: 'Learn',
    to: '/learn',
    menu: [
      { label: 'Capsules', to: '/learn#capsules' },
      { label: 'Reactive Timeline', to: '/learn#reactive-timeline' },
      { label: 'OBS Overlay', to: '/learn#obs-overlay' },
      { label: 'Shared Play', to: '/learn#shared-play' },
    ],
  },
  {
    label: 'Setup & Requirements',
    to: '/setup',
    menu: [
      { label: 'Windows', to: '/setup#windows' },
      { label: 'Linux', to: '/setup#linux' },
      { label: 'System Requirements', to: '/setup#requirements' },
    ],
  },
]

export function Navbar() {
  const [scrolled, setScrolled] = useState(false)
  useEffect(() => {
    const fn = () => setScrolled(window.scrollY > 40)
    window.addEventListener('scroll', fn, { passive: true })
    return () => window.removeEventListener('scroll', fn)
  }, [])

  return (
    <nav className={`navbar ${scrolled ? 'navbar--scrolled' : ''}`}>
      <Link to="/" className="navbar__logo">
        <img src="/icon.png" alt="Spectralis" className="navbar__icon" />
        <span>Spectralis</span>
      </Link>
      <div className="navbar__links">
        {NAV_ITEMS.map((item) => (
          <div className="navbar__item" key={item.label}>
            <NavLink
              to={item.to}
              className={({ isActive }) => `navbar__item-trigger${isActive ? ' navbar__link--active' : ''}`}
            >
              {item.label}
            </NavLink>
            <div className="navbar__dropdown">
              {item.menu.map((sub) => (
                <Link key={sub.to} to={sub.to} className="navbar__dropdown-link">
                  {sub.label}
                </Link>
              ))}
            </div>
          </div>
        ))}
        <a href="https://github.com/dareto-dream/spectralis" target="_blank" rel="noreferrer" className="navbar__gh">
          <GitFork size={14} />
          GitHub
          <ArrowUpRight size={12} className="navbar__gh-arrow" />
        </a>
        <NavLink
          to="/setup"
          className={({ isActive }) => `navbar__dl-link${isActive ? ' navbar__dl-link--active' : ''}`}
        >
          <Download size={13} />
          Download free
        </NavLink>
      </div>
    </nav>
  )
}
