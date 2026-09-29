import { APP_VERSION, FAQ, FEATURES, SCREENSHOTS } from '../data/site.jsx'
import { ARTICLES } from '../data/articles.jsx'
import { SITE_URL } from './seo.js'

// Everything routeMeta() needs, pulled from the same data the pages render.
export const SEO_DATA = {
  version: APP_VERSION,
  features: FEATURES.map(({ title, body }) => ({ title, body })),
  screenshots: SCREENSHOTS.map(({ src }) => `${SITE_URL}${src}`),
  faq: FAQ,
  articles: ARTICLES.map(({ slug, title, summary }) => ({ slug, title, summary })),
}
