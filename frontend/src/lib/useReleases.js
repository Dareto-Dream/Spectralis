import { useEffect, useState } from 'react'
import { API_BASE } from '../data/site.jsx'
import { RELEASES_PATH, normalizeReleases, splitReleases } from './releases.js'

const EMPTY = { latest: null, older: [], beta: null }
let cached = null

/**
 * The release index, split for the downloads page. Starts empty and fills in once; a failed or
 * slow request leaves it empty, and callers fall back to the version baked in at build time.
 */
export function useReleases() {
  const [releases, setReleases] = useState(cached ?? EMPTY)

  useEffect(() => {
    if (cached) return undefined
    let cancelled = false
    fetch(`${API_BASE}${RELEASES_PATH}`)
      .then(res => { if (!res.ok) throw new Error(String(res.status)); return res.json() })
      .then(data => {
        cached = splitReleases(normalizeReleases(data))
        if (!cancelled) setReleases(cached)
      })
      .catch(() => { /* the static page still works */ })
    return () => { cancelled = true }
  }, [])

  return releases
}
