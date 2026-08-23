import { useState, useEffect } from 'react'
import { NavLink, Link } from 'react-router-dom'
import { Download, GitFork, ArrowUpRight, ChevronDown } from 'lucide-react'

const NAV_ITEMS = [
  {
    label: 'Features',
    to: '/features',
    dropdown: true,
    menu: [
      { label: 'All Features', to: '/features#features' },
      { label: 'Visualizers', to: '/features#visualizers' },
      { label: 'FAQ', to: '/setup#faq' },
    ],
  },
  {
    label: 'Download',
    to: '/setup',
    dropdown: false,
  },
  {
    label: 'Learn',
    to: '/learn',
    dropdown: false,
  },
  {
    label: 'Help & Manuals',
    to: '/learn',
    dropdown: true,
    menu: [],
    noHighlight: true,
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
      <div className="navbar__center">
        {NAV_ITEMS.map((item) => (
          <div className="navbar__item" key={item.label}>
            <NavLink
              to={item.to}
              className={({ isActive }) => `navbar__item-trigger${isActive && !item.noHighlight ? ' navbar__link--active' : ''}`}
            >
              {item.label}
              {item.dropdown && <ChevronDown size={12} className="navbar__item-arrow" />}
            </NavLink>
            {item.dropdown && (
              <div className="navbar__dropdown">
                {item.menu.map((sub) => (
                  <Link
                    key={sub.to}
                    to={sub.to}
                    className="navbar__dropdown-link"
                    onClick={(e) => e.currentTarget.blur()}
                  >
                    {sub.label}
                  </Link>
                ))}
              </div>
            )}
          </div>
        ))}
      </div>
      <div className="navbar__actions">
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
