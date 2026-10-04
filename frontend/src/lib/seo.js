// Per-route head metadata + schema.org JSON-LD. Shared by the client (Seo.jsx)
// and the build-time prerender (seo-plugin.js), so both emit the same tags.
// Same @id scheme as www.deltavdevs.com so crawlers can stitch the graph together.
export const SITE_URL = 'https://spectralis.deltavdevs.com'
const MAIN_URL = 'https://www.deltavdevs.com'
const ORG_ID = `${MAIN_URL}/#organization`
const PERSON_ID = `${MAIN_URL}/#deltavortex`
const WEBSITE_ID = `${SITE_URL}/#website`
const APP_ID = `${SITE_URL}/#app`

export const SITE_NAME = 'Spectralis'
export const SOCIAL_IMAGE = `${SITE_URL}/og-image.jpg`
export const SOCIAL_IMAGE_ALT = 'Spectralis playing a track on the Mirror Spectrum visualizer with synced lyrics'

const HOME_DESCRIPTION =
  'Spectralis is a free desktop audio player for Windows, macOS and Linux with real-time visualizers, synced lyrics, a live OBS overlay server, and signed .spectralis capsule releases.'

const STATIC_ROUTES = {
  '/': {
    title: 'Spectralis — Audio Player with Visualizers and Synced Lyrics',
    description: HOME_DESCRIPTION,
    kind: 'home',
  },
  '/features': {
    title: 'Features — Spectralis',
    description:
      'Everything in Spectralis: 15 real-time visualizers, synced lyrics, signed .spectralis capsules, an OBS overlay, Discord Rich Presence, Spotify, and embedded experiences.',
    kind: 'features',
  },
  '/learn': {
    title: 'Learn — How Spectralis Works',
    description:
      'Guides to the .spectralis capsule format, album worlds, the reactive timeline, embedded MP3 experiences, the OBS overlay server, and Shared Play.',
    kind: 'learn',
  },
  '/setup': {
    title: 'Download and Setup — Spectralis',
    description:
      'Download Spectralis for Windows 10/11, macOS 11+ (Apple Silicon and Intel) or Linux (AppImage). Free, no sign-in, updates itself.',
    kind: 'setup',
  },
  '/terms': {
    title: 'Terms of Service — Spectralis',
    description: 'Terms of Service for the Spectralis desktop app and its Shared Play services.',
    kind: 'legal',
  },
  '/privacy': {
    title: 'Privacy Policy — Spectralis',
    description: 'What data the Spectralis desktop app and its Shared Play services collect, and what stays on your device.',
    kind: 'legal',
  },
}

const NOT_FOUND = {
  title: 'Page not found — Spectralis',
  description: 'This page does not exist.',
  kind: 'notfound',
  noindex: true,
}

// Tools behind a login-ish token: they get a proper title but are never indexed, prerendered or put in the sitemap.
const PRIVATE_ROUTES = {
  '/queue': {
    title: 'Streamer Queue dashboard — Spectralis',
    description: 'Moderate and monitor your Spectralis Streamer Queue.',
  },
}

export const STATIC_PATHS = Object.keys(STATIC_ROUTES)

const compact = (obj) =>
  Object.fromEntries(
    Object.entries(obj).filter(([, v]) => v !== undefined && v !== null && v !== '' && !(Array.isArray(v) && !v.length)),
  )

const breadcrumb = (items) => ({
  '@type': 'BreadcrumbList',
  itemListElement: items.map(([name, path], i) => ({
    '@type': 'ListItem',
    position: i + 1,
    name,
    item: `${SITE_URL}${path}`,
  })),
})

// Only the fields shared with the main site's org/person nodes; the full
// versions (logo, sameAs) live on www.deltavdevs.com under the same @ids.
const publisherNodes = [
  { '@type': 'Organization', '@id': ORG_ID, name: 'DeltaVDevs', url: MAIN_URL },
  { '@type': 'Person', '@id': PERSON_ID, name: 'deltavortex', url: `${MAIN_URL}/about` },
]

const websiteNode = {
  '@type': 'WebSite',
  '@id': WEBSITE_ID,
  name: SITE_NAME,
  url: `${SITE_URL}/`,
  description: HOME_DESCRIPTION,
  inLanguage: 'en-US',
  publisher: { '@id': ORG_ID },
}

const appNode = ({ version, featureList, screenshots }) =>
  compact({
    '@type': 'SoftwareApplication',
    '@id': APP_ID,
    name: SITE_NAME,
    url: `${SITE_URL}/`,
    description: HOME_DESCRIPTION,
    applicationCategory: 'MultimediaApplication',
    applicationSubCategory: 'Audio player and visualizer',
    operatingSystem: 'Windows 10, Windows 11, macOS 11 or newer, Linux x86_64',
    softwareVersion: version,
    downloadUrl: `${SITE_URL}/setup`,
    installUrl: `${SITE_URL}/setup`,
    image: `${SITE_URL}/icon.png`,
    screenshot: screenshots,
    featureList,
    isAccessibleForFree: true,
    license: 'https://opensource.org/licenses/MIT',
    offers: { '@type': 'Offer', price: '0', priceCurrency: 'USD' },
    author: { '@id': PERSON_ID },
    publisher: { '@id': ORG_ID },
    codeRepository: 'https://github.com/dareto-dream/spectralis',
  })

const webPage = (type, path, name, description, extra = {}) =>
  compact({
    '@type': type,
    '@id': `${SITE_URL}${path}#page`,
    url: `${SITE_URL}${path}`,
    name,
    description,
    inLanguage: 'en-US',
    isPartOf: { '@id': WEBSITE_ID },
    about: { '@id': APP_ID },
    ...extra,
  })

const graph = (...nodes) => ({ '@context': 'https://schema.org', '@graph': nodes.flat().filter(Boolean) })

// data: { version, features, screenshots, faq, articles }
//   features:    [{ title, body }]
//   screenshots: [absolute image urls]
//   faq:         [{ q, a }]
//   articles:    [{ slug, title, summary }]
export function routeMeta(pathname, data) {
  const path = pathname.length > 1 ? pathname.replace(/\/+$/, '') : pathname
  const app = appNode({
    version: data.version,
    featureList: data.features.map((f) => f.title),
    screenshots: data.screenshots,
  })

  const slug = /^\/learn\/([^/]+)$/.exec(path)?.[1]
  const article = slug && data.articles.find((a) => a.slug === slug)
  if (article) {
    const description = article.summary
    const title = `${article.title} — Spectralis`
    return {
      title,
      description,
      canonical: `${SITE_URL}${path}`,
      type: 'article',
      image: SOCIAL_IMAGE,
      jsonld: graph(
        compact({
          '@type': 'TechArticle',
          '@id': `${SITE_URL}${path}#article`,
          headline: article.title,
          description,
          url: `${SITE_URL}${path}`,
          mainEntityOfPage: `${SITE_URL}${path}`,
          image: SOCIAL_IMAGE,
          inLanguage: 'en-US',
          about: { '@id': APP_ID },
          author: { '@id': PERSON_ID },
          publisher: { '@id': ORG_ID },
          isPartOf: { '@id': WEBSITE_ID },
        }),
        breadcrumb([['Spectralis', '/'], ['Learn', '/learn'], [article.title, path]]),
        websiteNode,
        publisherNodes,
      ),
    }
  }

  const privateRoute = PRIVATE_ROUTES[path]
  if (privateRoute) {
    return { ...privateRoute, canonical: `${SITE_URL}${path}`, type: 'website', image: SOCIAL_IMAGE, noindex: true, jsonld: null }
  }

  const route = STATIC_ROUTES[path]
  if (!route) return { ...NOT_FOUND, canonical: `${SITE_URL}${path}`, image: SOCIAL_IMAGE, type: 'website', jsonld: null }

  const base = { title: route.title, description: route.description, canonical: `${SITE_URL}${path}`, type: 'website', image: SOCIAL_IMAGE }
  const crumbs = (name) => breadcrumb([['Spectralis', '/'], [name, path]])
  const tail = [websiteNode, publisherNodes]

  switch (route.kind) {
    case 'home':
      return { ...base, jsonld: graph(webPage('WebPage', '/', route.title, route.description), app, ...tail) }
    case 'features':
      return {
        ...base,
        jsonld: graph(
          webPage('WebPage', path, 'Spectralis features', route.description, {
            mainEntity: {
              '@type': 'ItemList',
              itemListElement: data.features.map((f, i) => ({
                '@type': 'ListItem',
                position: i + 1,
                name: f.title,
                description: f.body,
              })),
            },
          }),
          crumbs('Features'),
          app,
          ...tail,
        ),
      }
    case 'learn':
      return {
        ...base,
        jsonld: graph(
          webPage('CollectionPage', path, 'How Spectralis works', route.description, {
            mainEntity: {
              '@type': 'ItemList',
              itemListElement: data.articles.map((a, i) => ({
                '@type': 'ListItem',
                position: i + 1,
                name: a.title,
                url: `${SITE_URL}/learn/${a.slug}`,
              })),
            },
          }),
          crumbs('Learn'),
          ...tail,
        ),
      }
    case 'setup':
      return {
        ...base,
        jsonld: graph(
          webPage('WebPage', path, 'Download and setup', route.description),
          {
            '@type': 'FAQPage',
            '@id': `${SITE_URL}${path}#faq`,
            mainEntity: data.faq.map((f) => ({
              '@type': 'Question',
              name: f.q,
              acceptedAnswer: { '@type': 'Answer', text: f.a },
            })),
          },
          crumbs('Setup'),
          app,
          ...tail,
        ),
      }
    default:
      return {
        ...base,
        jsonld: graph(webPage('WebPage', path, route.title, route.description), crumbs(route.title.split(' — ')[0]), ...tail),
      }
  }
}

// Escape "<" so a description containing "</script>" can't break out of the tag.
export const jsonLdString = (jsonld) => JSON.stringify(jsonld).replace(/</g, '\\u003c')
