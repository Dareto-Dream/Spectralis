import { useParams, Navigate, Link } from 'react-router-dom'
import { ArrowLeft, ArrowRight } from 'lucide-react'
import { ARTICLES, getArticle } from '../data/articles.jsx'

export default function LearnArticle() {
  const { slug } = useParams()
  const article = getArticle(slug)

  if (!article) return <Navigate to="/learn" replace />

  const Icon = article.icon
  const index = ARTICLES.findIndex((a) => a.slug === slug)
  const prev = index > 0 ? ARTICLES[index - 1] : null
  const next = ARTICLES[(index + 1) % ARTICLES.length]

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
      <div className="learn-article__pager">
        {prev && (
          <Link to={`/learn/${prev.slug}`} className="learn-article__pager-item learn-article__pager-item--prev">
            <span className="learn-article__pager-label"><ArrowLeft size={13} /> Previous lesson</span>
            <span className="learn-article__pager-title">{prev.title}</span>
          </Link>
        )}
        <Link to={`/learn/${next.slug}`} className="learn-article__pager-item learn-article__pager-item--next">
          <span className="learn-article__pager-label">Next lesson <ArrowRight size={13} /></span>
          <span className="learn-article__pager-title">{next.title}</span>
        </Link>
      </div>
    </article>
  )
}
