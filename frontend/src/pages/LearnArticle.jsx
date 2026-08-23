import { useParams, Navigate, Link } from 'react-router-dom'
import { ArrowLeft } from 'lucide-react'
import { getArticle } from '../data/articles.jsx'

export default function LearnArticle() {
  const { slug } = useParams()
  const article = getArticle(slug)

  if (!article) return <Navigate to="/learn" replace />

  const Icon = article.icon

  return (
    <article className="learn-article section">
      <Link to="/learn" className="learn-article__back">
        <ArrowLeft size={14} /> Back to Learn
      </Link>
      <div className="learn-entry">
        <div className="learn-entry__copy">
          <div className="section__head">
            <span className="section__label">{article.label}</span>
            <h1 className="page-head__title learn-article__title">
              <Icon size={30} className="learn-entry__icon" />
              {article.title}
            </h1>
          </div>
          <div className="learn-entry__body">{article.body}</div>
        </div>
        {article.visual && (
          <div className="learn-entry__visual">
            <div className="cap-card">
              <div className="cap-card__bar">
                <span className="cap-card__dot" style={{ background: '#ef4444' }} />
                <span className="cap-card__dot" style={{ background: '#eab308' }} />
                <span className="cap-card__dot" style={{ background: '#22c55e' }} />
                <code className="cap-card__name">{article.visual.filename}</code>
              </div>
              <pre className="cap-card__code">{article.visual.code}</pre>
            </div>
          </div>
        )}
      </div>
    </article>
  )
}
