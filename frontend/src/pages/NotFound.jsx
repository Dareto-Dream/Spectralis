import { Link } from 'react-router-dom'

export default function NotFound() {
  return (
    <main className="page-head">
      <span className="section__label">404</span>
      <h1 className="page-head__title">That page doesn't exist.</h1>
      <p><Link to="/">Back to Spectralis</Link></p>
    </main>
  )
}
