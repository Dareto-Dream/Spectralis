import { useState, useEffect } from 'react'
import { NavLink, Link } from 'react-router-dom'
import { Download, GitFork, ArrowUpRight } from 'lucide-react'

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
        <NavLink to="/features" className={({ isActive }) => isActive ? 'navbar__link--active' : ''}>Features</NavLink>
        <NavLink to="/learn" className={({ isActive }) => isActive ? 'navbar__link--active' : ''}>Learn</NavLink>
        <NavLink to="/setup" className={({ isActive }) => isActive ? 'navbar__link--active' : ''}>Setup & Requirements</NavLink>
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
