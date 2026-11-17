import { useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import { Download, ArrowUpRight, ChevronDown } from 'lucide-react'
import { FAQ, APP_VERSION, DOWNLOAD_BASE, CHANGELOG_URL, RELEASES_PAGE_URL } from '../data/site.jsx'
import { useReleases } from '../lib/useReleases.js'
import { compareVersions, parseVersion } from '../lib/releases.js'
import './Setup.css'

const PLATFORM_LABELS = { 'win-x64': 'Windows', 'osx-arm64': 'Mac (Apple Silicon)', 'osx-x64': 'Mac (Intel)', 'linux-x64': 'Linux' }

function ReleaseDownloads({ release }) {
  return Object.entries(PLATFORM_LABELS)
    .filter(([rid]) => release.downloads[rid])
    .map(([rid, label]) => (
      <a key={rid} href={release.downloads[rid]} aria-label={`Download Spectralis ${release.version} for ${label}`}>
        {label}<Download size={13} aria-hidden="true" />
      </a>
    ))
}

function LatestChangelog({ latest }) {
  const [feed, setFeed] = useState(null)
  const [failed, setFailed] = useState(false)

  useEffect(() => {
    const controller = new AbortController()
    fetch(CHANGELOG_URL, { signal: controller.signal })
      .then(response => {
        if (!response.ok) throw new Error(String(response.status))
        return response.json()
      })
      .then(data => {
        if (!Array.isArray(data)) throw new Error('Invalid changelog')
        if (!controller.signal.aborted) setFeed(data)
      })
      .catch(() => { if (!controller.signal.aborted) setFailed(true) })
    return () => controller.abort()
  }, [])

  const releases = feed?.filter(release => {
    const version = parseVersion(release?.version)
    return version && !version.pre && typeof release.summary === 'string' && Array.isArray(release.groups)
  }).sort((a, b) => compareVersions(b.version, a.version))
  // Match the installer when its catalog has loaded; never put older notes under a newer version.
  const release = latest
    ? releases?.find(item => compareVersions(item.version, latest.version) === 0)
    : releases?.[0]

  return (
    <section className="setup-section setup-changelog" id="changelog" aria-labelledby="changelog-heading">
      <div className="setup-section__heading">
        <h2 id="changelog-heading">Latest changelog</h2>
        <a href={RELEASES_PAGE_URL} target="_blank" rel="noreferrer">
          All release notes<ArrowUpRight size={14} aria-hidden="true" />
        </a>
      </div>
      <div className="setup-changelog__body" aria-live="polite">
        {!failed && feed === null && <p role="status">Loading release notes…</p>}
        {(failed || (feed !== null && !release)) && (
          <p>Release notes couldn’t be loaded. <a href={latest?.notesUrl || RELEASES_PAGE_URL} target="_blank" rel="noreferrer">Read them on GitHub</a>.</p>
        )}
        {release && (
          <article>
            <h3 className="setup-changelog__title">v{release.version}{release.label && <span>{release.label}</span>}</h3>
            <p className="setup-changelog__summary">{release.summary}</p>
            {release.groups.filter(group => group && typeof group.title === 'string').map((group, index) => (
              <section className="setup-changelog__group" key={`${group.title}-${index}`}>
                <h4>{group.title}</h4>
                <ul>
                  {Array.isArray(group.bullets) && group.bullets.filter(item => typeof item === 'string').map((item, i) => <li key={i}>{item}</li>)}
                </ul>
              </section>
            ))}
          </article>
        )}
      </div>
    </section>
  )
}

function PlatformDownload({ id, name, requirements, href, format, children, notes, downloads }) {
  return (
    <section className="setup-platform" id={id} aria-labelledby={`${id}-heading`}>
      <div className="setup-platform__name">
        <h2 id={`${id}-heading`}>{name}</h2>
        <p>{requirements}</p>
      </div>
      <div className="setup-platform__info">
        <p>{children}</p>
        <details className="setup-install">
          <summary>Installation notes<ChevronDown size={14} aria-hidden="true" /></summary>
          <div>{notes}</div>
        </details>
      </div>
      <div className="setup-platform__downloads">
        {downloads ?? <a href={href} className="setup-download" aria-label={`Download Spectralis for ${name}`}>
          <Download size={17} aria-hidden="true" />Download <span>{format}</span>
        </a>}
      </div>
    </section>
  )
}

export default function Setup() {
  const { latest, older, beta } = useReleases()
  const version = latest?.version ?? APP_VERSION
  const nativeMac = latest?.downloads['osx-arm64']

  return (
    <main className="setup-page">
      <header className="setup-header">
        <h1>Download Spectralis</h1>
        <p>Free. No account needed.</p>
        <div className="setup-header__release">
          <span>Version {version}</span>
          {latest?.date && <time dateTime={latest.date}>{latest.date}</time>}
          <a href="#changelog">What’s changed</a>
        </div>
      </header>

      <div className="setup-platforms" id="requirements">
        <PlatformDownload
          id="windows" name="Windows" requirements="Windows 10 or 11 · 64-bit"
          href={latest?.downloads['win-x64'] ?? `${DOWNLOAD_BASE}/Spectralis-win-x64-Setup.exe`}
          format=".exe"
          notes={<p>Run <code>Setup.exe</code>. Spectralis installs to <code>%LocalAppData%\Spectralis</code> and opens when it’s ready. The .NET runtime and WebView2 are bundled.</p>}
        >
          Run the installer. No administrator access needed.
        </PlatformDownload>
        <PlatformDownload
          id="macos" name="macOS" requirements={nativeMac ? 'Apple Silicon: 13+ · Intel: 12+' : 'Intel / Rosetta 2'}
          href={latest?.downloads['osx-x64'] ?? `${DOWNLOAD_BASE}/Spectralis-osx-x64-Setup.pkg`}
          format=".pkg"
          downloads={nativeMac ? <>
            <a href={nativeMac} className="setup-download" aria-label="Download Spectralis for Apple Silicon"><Download size={17} aria-hidden="true" />Apple Silicon<span>.pkg</span></a>
            <a href={latest.downloads['osx-x64']} className="setup-download" aria-label="Download Spectralis for Intel Mac"><Download size={17} aria-hidden="true" />Intel<span>.pkg</span></a>
          </> : undefined}
          notes={<><p>Open the <code>.pkg</code> and follow the installer. {nativeMac ? 'M-series Macs use the Apple Silicon package, which requires macOS 13 or newer. If you installed an Intel build before, quit Spectralis and install the Apple Silicon package once to switch to native updates.' : 'Apple Silicon Macs can run the Intel package through Rosetta 2.'}</p><p>If macOS blocks the unsigned installer, try opening it first, then go to <strong>System Settings → Privacy &amp; Security → Open Anyway</strong>.</p></>}
        >
          {nativeMac ? 'Native builds for M-series and Intel Macs.' : 'Intel build; Apple Silicon uses Rosetta 2.'} The installers are unsigned and not notarized.
        </PlatformDownload>
        <PlatformDownload
          id="linux" name="Linux" requirements="x86_64 · AppImage"
          href={latest?.downloads['linux-x64'] ?? `${DOWNLOAD_BASE}/Spectralis-linux-x64.AppImage`}
          format=".AppImage"
          notes={<><p>Run <code>chmod +x Spectralis-linux-x64.AppImage</code>, then double-click the file or launch it from your terminal. Requires glibc 2.17 or newer.</p><p>If FUSE is unavailable, launch with <code>--appimage-extract-and-run</code>. AppImageLauncher is optional for adding it to your app menu.</p></>}
        >
          Make the file executable, then open it. No root access needed.
        </PlatformDownload>
      </div>
      <p className="setup-updates">Already installed? Manage updates in the app under Settings → Updates.</p>

      <LatestChangelog latest={latest} />

      <section className="setup-section" id="versions" aria-labelledby="versions-heading">
        <div className="setup-section__heading"><h2 id="versions-heading">Other versions</h2></div>
        <div className="setup-versions">
          {beta && (
            <div className="setup-beta">
              <h3>Beta {beta.version}</h3>
              <p>Preview builds can break. You can also switch to the Beta channel in Settings → Updates.</p>
              <div className="setup-version-links"><ReleaseDownloads release={beta} /></div>
            </div>
          )}
          <details className="setup-archive">
            <summary>Older releases<ChevronDown size={17} aria-hidden="true" /></summary>
            {older.length > 0 ? (
              <ul className="setup-version-list" aria-label="Older releases">
                {older.map(release => (
                  <li key={release.version}>
                    <div className="setup-version-list__version">v{release.version}<time dateTime={release.date}>{release.date}</time></div>
                    <div className="setup-version-links">
                      <ReleaseDownloads release={release} />
                      {release.notesUrl && <a href={release.notesUrl} target="_blank" rel="noreferrer">Notes<ArrowUpRight size={13} aria-hidden="true" /></a>}
                    </div>
                  </li>
                ))}
              </ul>
            ) : <p>Older downloads are available on <a href={RELEASES_PAGE_URL} target="_blank" rel="noreferrer">GitHub Releases</a>.</p>}
            <p className="setup-versions__hint">You can also install an older version in Settings → Updates. The app warns you before turning off automatic updates and keeps the separate Update Spectralis tool available to get you back to the latest release.</p>
          </details>
        </div>
      </section>

      <section className="setup-section" id="faq" aria-labelledby="faq-heading">
        <div className="setup-section__heading"><h2 id="faq-heading">Questions</h2></div>
        <div className="setup-questions">
          {FAQ.map(({ q, a }) => (
            <details key={q}>
              <summary>{q}<ChevronDown size={17} aria-hidden="true" /></summary>
              <p>{a}</p>
            </details>
          ))}
        </div>
      </section>

      <footer className="setup-footer">
        <Link to="/">Back to home</Link>
        <a href="https://github.com/Dareto-Dream/Spectralis" target="_blank" rel="noreferrer">Source on GitHub<ArrowUpRight size={14} aria-hidden="true" /></a>
      </footer>
    </main>
  )
}
