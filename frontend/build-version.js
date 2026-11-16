import process from 'node:process'

// Works out the app version to bake into a site build: the newest stable release in the release
// index, so prerendered pages, structured data and static copy pick up a release without anyone
// editing the site. If the backend can't be reached the build still succeeds with the fallback in
// src/data/site.jsx, and the pages correct themselves in the browser (see src/lib/useReleases.js).

const RELEASES_URL = 'https://spectralis-api.deltavdevs.com/spectralis/v1/releases'

let memo

export function resolveAppVersion() {
  memo ??= (async () => {
    if (process.env.SPECTRALIS_APP_VERSION) return process.env.SPECTRALIS_APP_VERSION
    try {
      const res = await fetch(RELEASES_URL, { signal: AbortSignal.timeout(5000) })
      if (!res.ok) return null
      const { normalizeReleases, splitReleases } = await import('./src/lib/releases.js')
      return splitReleases(normalizeReleases(await res.json())).latest?.version ?? null
    } catch {
      return null
    }
  })()
  return memo
}

/** The `define` entries for a Vite config; empty when the version could not be resolved. */
export async function versionDefine() {
  const version = await resolveAppVersion()
  return version ? { __APP_VERSION__: JSON.stringify(version) } : {}
}
