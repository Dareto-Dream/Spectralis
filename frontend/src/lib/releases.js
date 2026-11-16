// The release index (`/spectralis/v1/releases`), written by the release pipeline on every merge
// into main. The site reads it for the current version, the list of older versions and the newest
// beta, so none of those need a site change when a release ships. Pure functions here; the React
// hook is in useReleases.js.

export const RELEASES_PATH = '/spectralis/v1/releases'

const PLATFORMS = ['win-x64', 'linux-x64', 'osx-x64', 'osx-arm64']

// Downloads only ever point at our own release locations, whatever the index says.
const TRUSTED = [
  'https://github.com/Dareto-Dream/Spectralis/releases/',
  'https://spectralis-cdn.deltavdevs.com/',
]

export function trustedUrl(url) {
  if (typeof url !== 'string') return ''
  try {
    const href = new URL(url).href
    return TRUSTED.some(prefix => href.startsWith(prefix)) ? href : ''
  } catch {
    return ''
  }
}

export function parseVersion(text) {
  const m = /^v?(\d+)\.(\d+)\.(\d+)(?:-([0-9A-Za-z.-]+))?$/.exec(String(text ?? '').trim())
  return m ? { major: +m[1], minor: +m[2], patch: +m[3], pre: m[4] ?? null } : null
}

/** Semver ordering; a pre-release sorts below its release. Unparseable input sorts lowest. */
export function compareVersions(a, b) {
  const x = parseVersion(a)
  const y = parseVersion(b)
  if (!x || !y) return (x ? 1 : 0) - (y ? 1 : 0)
  for (const k of ['major', 'minor', 'patch']) if (x[k] !== y[k]) return x[k] - y[k]
  if (x.pre === y.pre) return 0
  if (x.pre === null) return 1
  if (y.pre === null) return -1
  const xs = x.pre.split('.')
  const ys = y.pre.split('.')
  for (let i = 0; i < Math.max(xs.length, ys.length); i++) {
    if (xs[i] === undefined) return -1
    if (ys[i] === undefined) return 1
    const xn = /^\d+$/.test(xs[i])
    const yn = /^\d+$/.test(ys[i])
    if (xn && yn) { if (+xs[i] !== +ys[i]) return +xs[i] - +ys[i] }
    else if (xn !== yn) return xn ? -1 : 1
    else if (xs[i] !== ys[i]) return xs[i] < ys[i] ? -1 : 1
  }
  return 0
}

/**
 * Cleans whatever the endpoint returned into rows the page can render without further checks:
 * valid versions only, newest first, download links limited to trusted locations.
 */
export function normalizeReleases(data) {
  if (!Array.isArray(data)) return []
  const rows = []
  for (const item of data) {
    if (!item || typeof item !== 'object' || !parseVersion(item.version)) continue
    const downloads = {}
    for (const rid of PLATFORMS) {
      const installer = trustedUrl(item.platforms?.[rid]?.installer)
      if (installer) downloads[rid] = installer
    }
    rows.push({
      version: String(item.version).replace(/^v/, ''),
      beta: item.channel === 'beta',
      date: typeof item.date === 'string' ? item.date : '',
      notesUrl: trustedUrl(item.notesUrl),
      downloads,
    })
  }
  return rows.sort((a, b) => compareVersions(b.version, a.version))
}

/**
 * Splits the index into what the downloads page shows: the current stable release, the stable
 * releases before it, and the newest beta if it is ahead of stable (a beta that stable has
 * overtaken is not worth offering).
 */
export function splitReleases(rows) {
  const stable = rows.filter(r => !r.beta)
  const latest = stable[0] ?? null
  const newestBeta = rows.find(r => r.beta) ?? null
  const beta = newestBeta && (!latest || compareVersions(newestBeta.version, latest.version) > 0) ? newestBeta : null
  return {
    latest,
    older: stable.slice(1).filter(r => Object.keys(r.downloads).length > 0),
    beta,
  }
}
