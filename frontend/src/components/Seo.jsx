import { useEffect } from 'react'
import { useLocation } from 'react-router-dom'
import { routeMeta, jsonLdString, SITE_NAME, SOCIAL_IMAGE_ALT } from '../lib/seo.js'
import { SEO_DATA } from '../lib/seoData.js'

// The prerender (seo-plugin.js) bakes these tags into each route's HTML with
// data-seo attributes. This keeps them in step while navigating client-side.
function upsert(selector, create, apply) {
  let el = document.head.querySelector(selector)
  if (!el) {
    el = create()
    el.setAttribute('data-seo', '')
    document.head.appendChild(el)
  }
  apply(el)
}

const meta = (attr, key, value) =>
  upsert(
    `meta[${attr}="${key}"][data-seo]`,
    () => document.createElement('meta'),
    (el) => {
      el.setAttribute(attr, key)
      el.setAttribute('content', value)
    },
  )

export function Seo() {
  const { pathname } = useLocation()

  useEffect(() => {
    const m = routeMeta(pathname, SEO_DATA)
    document.title = m.title

    meta('name', 'description', m.description)
    meta('name', 'robots', m.noindex ? 'noindex,nofollow' : 'index,follow,max-image-preview:large')
    meta('property', 'og:type', m.type)
    meta('property', 'og:site_name', SITE_NAME)
    meta('property', 'og:title', m.title)
    meta('property', 'og:description', m.description)
    meta('property', 'og:url', m.canonical)
    meta('property', 'og:image', m.image)
    meta('property', 'og:image:width', '1200')
    meta('property', 'og:image:height', '630')
    meta('property', 'og:image:alt', SOCIAL_IMAGE_ALT)
    meta('name', 'twitter:card', 'summary_large_image')
    meta('name', 'twitter:title', m.title)
    meta('name', 'twitter:description', m.description)
    meta('name', 'twitter:image', m.image)
    meta('name', 'twitter:image:alt', SOCIAL_IMAGE_ALT)

    upsert(
      'link[rel="canonical"][data-seo]',
      () => Object.assign(document.createElement('link'), { rel: 'canonical' }),
      (el) => el.setAttribute('href', m.canonical),
    )

    const ld = document.head.querySelector('script[type="application/ld+json"][data-seo]')
    if (m.jsonld) {
      upsert(
        'script[type="application/ld+json"][data-seo]',
        () => Object.assign(document.createElement('script'), { type: 'application/ld+json' }),
        (el) => { el.textContent = jsonLdString(m.jsonld) },
      )
    } else {
      ld?.remove()
    }
  }, [pathname])

  return null
}
