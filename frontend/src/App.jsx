import { useEffect } from 'react'
import { Routes, Route, Navigate, useLocation } from 'react-router-dom'
import { Navbar } from './components/Navbar.jsx'
import { Footer } from './components/Footer.jsx'
import Home from './pages/Home.jsx'
import Features from './pages/Features.jsx'
import Learn from './pages/Learn.jsx'
import LearnArticle from './pages/LearnArticle.jsx'
import Setup from './pages/Setup.jsx'
import Terms from './pages/Terms.jsx'
import Privacy from './pages/Privacy.jsx'
import './App.css'

function ScrollToTop() {
  const { pathname, hash } = useLocation()
  useEffect(() => {
    if (hash) {
      const id = hash.slice(1)
      // wait a frame so the target page has actually rendered its sections
      requestAnimationFrame(() => {
        document.getElementById(id)?.scrollIntoView({ behavior: 'smooth', block: 'start' })
      })
      return
    }
    window.scrollTo({ top: 0, behavior: 'instant' })
  }, [pathname, hash])
  return null
}

export default function App() {
  return (
    <>
      <ScrollToTop />
      <Navbar />
      <Routes>
        <Route path="/" element={<Home />} />
        <Route path="/features" element={<Features />} />
        <Route path="/learn" element={<Learn />} />
        <Route path="/learn/:slug" element={<LearnArticle />} />
        <Route path="/setup" element={<Setup />} />
        <Route path="/downloads" element={<Navigate to="/setup" replace />} />
        <Route path="/terms" element={<Terms />} />
        <Route path="/privacy" element={<Privacy />} />
      </Routes>
      <Footer />
    </>
  )
}
