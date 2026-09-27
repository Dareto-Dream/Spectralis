import { readFileSync } from 'node:fs'
import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'

const SITE_ORIGIN = 'https://spectralis.deltavdevs.com'
const STATIC_ROUTES = ['/', '/features', '/setup', '/learn', '/terms', '/privacy']

// articles.jsx is full of JSX so node can't import it here; the slugs are plain strings, so pull them out directly
function sitemap() {
  return {
    name: 'sitemap',
    apply: 'build',
    generateBundle() {
      const articles = readFileSync(new URL('./src/data/articles.jsx', import.meta.url), 'utf8')
      const slugs = [...articles.matchAll(/^\s*slug:\s*'([^']+)'/gm)].map(([, slug]) => `/learn/${slug}`)
      const urls = [...STATIC_ROUTES, ...slugs]
        .map((path) => `  <url><loc>${SITE_ORIGIN}${path}</loc></url>`)
        .join('\n')
      this.emitFile({
        type: 'asset',
        fileName: 'sitemap.xml',
        source: `<?xml version="1.0" encoding="UTF-8"?>\n<urlset xmlns="http://www.sitemaps.org/schemas/sitemap/0.9">\n${urls}\n</urlset>\n`,
      })
    },
  }
}

// https://vite.dev/config/
export default defineConfig({
  plugins: [react(), sitemap()],
})
