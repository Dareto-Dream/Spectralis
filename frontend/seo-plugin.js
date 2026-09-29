import { mkdir, readFile, writeFile } from 'node:fs/promises'
import { dirname, join, resolve } from 'node:path'
import { createServer } from 'vite'
import react from '@vitejs/plugin-react'

const START = '<!--seo:start-->'
const END = '<!--seo:end-->'
const escape = (v) => String(v ?? '').replace(/[&<>"']/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]))

// Head tags for one route, all tagged data-seo so Seo.jsx can update them in place.
function headTags(m, { SITE_NAME, SOCIAL_IMAGE_ALT, jsonLdString }) {
  const meta = (attr, key, value) => `<meta ${attr}="${key}" content="${escape(value)}" data-seo>`
  return [
    `<title>${escape(m.title)}</title>`,
    meta('name', 'description', m.description),
    meta('name', 'robots', m.noindex ? 'noindex,nofollow' : 'index,follow,max-image-preview:large'),
    `<link rel="canonical" href="${escape(m.canonical)}" data-seo>`,
    meta('property', 'og:type', m.type),
    meta('property', 'og:site_name', SITE_NAME),
    meta('property', 'og:title', m.title),
    meta('property', 'og:description', m.description),
    meta('property', 'og:url', m.canonical),
    meta('property', 'og:image', m.image),
    meta('property', 'og:image:width', '1200'),
    meta('property', 'og:image:height', '630'),
    meta('property', 'og:image:alt', SOCIAL_IMAGE_ALT),
    meta('property', 'og:locale', 'en_US'),
    meta('name', 'twitter:card', 'summary_large_image'),
    meta('name', 'twitter:title', m.title),
    meta('name', 'twitter:description', m.description),
    meta('name', 'twitter:image', m.image),
    meta('name', 'twitter:image:alt', SOCIAL_IMAGE_ALT),
    m.jsonld ? `<script type="application/ld+json" data-seo>${jsonLdString(m.jsonld)}</script>` : '',
  ].filter(Boolean).join('\n    ')
}

// After the build, writes dist/<route>/index.html for every known route with its
// own title, description, canonical, social tags and JSON-LD already in the
// HTML (crawlers and link unfurlers don't run the app), plus sitemap.xml.
export default function seoPrerender() {
  let outDir, root
  return {
    name: 'spectralis-seo-prerender',
    apply: 'build',
    configResolved(config) {
      root = config.root
      outDir = resolve(config.root, config.build.outDir)
    },
    async closeBundle() {
      const server = await createServer({
        root, configFile: false, plugins: [react()], appType: 'custom', logLevel: 'silent',
        server: { middlewareMode: true }, optimizeDeps: { noDiscovery: true, include: [] },
      })
      try {
        const seo = await server.ssrLoadModule('/src/lib/seo.js')
        const { SEO_DATA } = await server.ssrLoadModule('/src/lib/seoData.js')
        const template = await readFile(join(outDir, 'index.html'), 'utf8')
        if (!template.includes(START) || !template.includes(END)) throw new Error('index.html is missing the seo:start/seo:end markers')

        const paths = [...seo.STATIC_PATHS, ...SEO_DATA.articles.map((a) => `/learn/${a.slug}`)]
        for (const path of paths) {
          const m = seo.routeMeta(path, SEO_DATA)
          const html = template.slice(0, template.indexOf(START)) + `${START}\n    ${headTags(m, seo)}\n    ${END}` + template.slice(template.indexOf(END) + END.length)
          const file = path === '/' ? join(outDir, 'index.html') : join(outDir, path, 'index.html')
          await mkdir(dirname(file), { recursive: true })
          await writeFile(file, html)
        }

        const urls = paths.map((p) => `  <url><loc>${escape(seo.SITE_URL + (p === '/' ? '/' : p))}</loc></url>`).join('\n')
        await writeFile(join(outDir, 'sitemap.xml'), `<?xml version="1.0" encoding="UTF-8"?>\n<urlset xmlns="http://www.sitemaps.org/schemas/sitemap/0.9">\n${urls}\n</urlset>\n`)
        console.log(`seo: prerendered ${paths.length} routes + sitemap.xml`)
      } finally {
        await server.close()
      }
    },
  }
}
