import { Link } from 'react-router-dom'
import { ArrowRight } from 'lucide-react'
import { ARTICLES } from '../data/articles.jsx'

function ArticleCard({ article }) {
  const Icon = article.icon
  return (
    <Link to={`/learn/${article.slug}`} className="article-card">
      <div className="article-thumb">
        <div className="article-thumb__bar">
          <span className="article-thumb__dot" style={{ background: '#ef4444' }} />
          <span className="article-thumb__dot" style={{ background: '#eab308' }} />
          <span className="article-thumb__dot" style={{ background: '#22c55e' }} />
          <code className="article-thumb__ext">{article.ext}</code>
        </div>
        <div className="article-thumb__body">
          <Icon size={34} className="article-thumb__icon" />
        </div>
      </div>
      <div className="article-card__copy">
        <span className="section__label">{article.label}</span>
        <h3 className="article-card__title">{article.title}</h3>
        <p className="article-card__summary">{article.summary}</p>
        <span className="article-card__cta">
          Read more <ArrowRight size={14} />
        </span>
      </div>
    </Link>
  )
}

export default function Learn() {
  return (
    <>
      <div className="page-head">
        <span className="section__label">Learn</span>
        <h1 className="page-head__title">How Spectralis<br />actually works.</h1>
      </div>
      <section className="section article-grid">
        {ARTICLES.map((article) => (
          <ArticleCard key={article.slug} article={article} />
        ))}
      </section>
    </>
  )
}
