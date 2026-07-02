import { useState, useEffect } from 'react'
import {
  Waves, Radio, Zap, Shield, Download, GitFork,
  ArrowRight, ArrowUpRight, FileCode2, Package, MonitorPlay,
  BarChart3, Activity, Layers, Sparkles, Music, Mic2,
  Terminal, RefreshCw, ListMusic, Globe, Crosshair, Dices,
  Trophy, Minimize2, Palette, Users, NotebookPen, Bug,
} from 'lucide-react'
import { VisualizerDemo } from './VisualizerDemo'
import './App.css'

// ── data ─────────────────────────────────────────────────────────────────────

const FEATURES = [
  { num: '01', icon: Waves,      title: 'Cinematic Visualizers',  body: '11 built-in animated visualizers at 60 FPS — radial spectrums, spinning disks, 3D spheres, oscilloscopes, and more. Each one reacts to every beat in real time.' },
  { num: '02', icon: Mic2,       title: 'Synced Lyrics',          body: 'Real-time lyric sync from .lrc sidecars, embedded LRC text, or structured lyric data baked into the file.' },
  { num: '03', icon: Package,    title: '.spectralis Capsules',    body: 'Ed25519-signed artist packages — audio, art, lyrics, a reactive timeline, and WASM visualizers — all in one cryptographically verified file.' },
  { num: '04', icon: MonitorPlay,title: 'OBS Overlay',            body: '11 layout presets served live at localhost:5128 via SSE. High-DPI spectrum canvas, artwork cache-busting. No polling, no page reloads.' },
  { num: '05', icon: Radio,      title: 'Discord Rich Presence',  body: 'Publish what you\'re hearing on Discord with a Spectralis download link and a Listen Together button via Shared Play.' },
  { num: '06', icon: Music,      title: 'Spotify Integration',    body: 'Link your account and Spectralis registers as a native Spotify device — OBS-ready, no extra audio routing, no separate process.' },
  { num: '07', icon: Zap,        title: 'Reactive Timeline',      body: 'Per-track .spectralis-reactive.json sidecars drive smooth parameter transitions synchronized frame-by-frame to playback position.' },
  { num: '08', icon: FileCode2,  title: 'Embedded Experiences',   body: 'WASM visualizers, HTML overlays, Markdown content, and synced video — all living directly inside the audio file.' },
  { num: '09', icon: Shield,     title: 'Creator Trust',          body: 'Unknown creator keys trigger a one-time trust prompt. Revoked keys are always rejected. Capabilities are intersection-enforced against the CDN.' },
]

const FORMAT_LIST = ['MP3','FLAC','WAV','OGG','Opus','M4A','AAC','WMA','WebM','AIFF','MP4','AIFF']

const CHANGELOG_RELEASES = [
  {
    version: '5.2.0',
    label: 'Dead zones & notepads',
    date: 'Latest',
    summary: 'Dead zones now protect the app itself, not just your OBS layout — the visualizer and docked sidebars shift out of the way of your webcam or alerts. A new Notepads panel lets you jot notes while audio plays and embed them straight into the file. Plus a reworked spin wheel editor, a SoundCloud playback fix, and a fix for broken redeemable visualizer images.',
    metrics: ['App-wide dead zones', 'Notepads', 'Weighted spin wheel'],
    groups: [
      {
        icon: Crosshair,
        title: 'Dead Zones Go App-Wide',
        bullets: [
          "Dead zones aren't OBS-only anymore — they reposition the app's own layout too, so a webcam or alert box doesn't cover something you're actually using.",
          'The visualizer panel shifts away from any overlapping dead zone; docked sidebars (Queue, Lyrics, Song Wars, Notepads) lose height instead of width, so nothing wraps awkwardly.',
          'The dead-zone designer finally shows where its canvas edges actually are, plus a Preview toggle that overlays your current OBS widget positions next to where dead zones would push them.',
        ],
      },
      {
        icon: NotebookPen,
        title: 'Notepads',
        bullets: [
          'New Notepads panel in Now Playing — write notes while audio plays, keep several open as tabs, and pop any of them out into their own window.',
          "Save a notepad straight into the currently playing local file's tags; the note travels with the file and reopens automatically next time that track plays.",
          'Useful for tracking a manual queue without Streamer Queue, leaving improvement notes on a mix, or writing a description for whoever the file gets shared with.',
          'An Open Notepad button on the empty "nothing playing" screen too, since taking notes shouldn\'t require a track to be loaded.',
        ],
      },
      {
        icon: Dices,
        title: 'Randomizer Tools, Reworked',
        bullets: [
          'Wheel entries are now edited as plain text, one per line, instead of adding them one at a time.',
          'Each entry gets a settings flyout for color, font, and spin weighting — heavier entries get bigger slices and better odds.',
          'Save and reload named wheels for later use.',
        ],
      },
      {
        icon: Bug,
        title: 'Fixes',
        bullets: [
          "SoundCloud (and other remote sources) could leave the seek bar stuck on the previous track's end position while the next one loaded — position now resets immediately.",
          "Redeemable visualizer images that referenced assets by their module's binding name instead of the CDN manifest's literal asset key now resolve correctly.",
        ],
      },
    ],
  },
  {
    version: '5.1.5',
    label: 'Streamer toolkit',
    date: 'Previous',
    summary: 'A streamer-focused release: a standalone Streamer Queue with paid skips, OBS dead zones that keep overlays off your face cam, spin-the-wheel and coin-toss randomizer tools, a Song Wars overhaul, and a rewritten Shared Play with short room codes.',
    metrics: ['Streamer Queue', 'OBS dead zones', 'Song Wars overhaul'],
    groups: [
      {
        icon: ListMusic,
        title: 'Streamer Queue',
        bullets: [
          'New standalone Streamer Queue nav section, decoupled from Shared Play — its own room IDs and owner tokens.',
          'Viewers submit requests by link or file upload from a browser page; approve, reject, edit, or reorder from the desktop app.',
          'Priority tiers for skip and super-skip requests, with optional Stripe pay-to-skip.',
          'Fingerprint-based duplicate/spam scoring never leaves the server — API responses never expose it.',
        ],
      },
      {
        icon: Globe,
        title: 'Album Worlds (.spectral)',
        bullets: [
          'Album worlds now open into a full world map instead of the old capsule page, with per-track JS-bridge navigation.',
          'Session tracking for played seconds and completion per world visit.',
          'Story renderer supports chapters plus a backstory fallback; capsule view shows a labeled backstory section.',
          'Legacy v1/v2 spectralis and v0 spectral capsules keep working.',
        ],
      },
      {
        icon: Crosshair,
        title: 'OBS Dead Zones',
        bullets: [
          'Draw dead zones over your webcam, alerts, or chat box in a new dead-zone designer.',
          'Overlay widgets automatically push away from dead zones instead of overlapping them.',
        ],
      },
      {
        icon: Dices,
        title: 'Randomizer Tools',
        bullets: [
          'New Randomizer Tools page with a coin flip and an animated spin wheel, side by side.',
          'Wheel canvas is fully responsive and fills the available view.',
        ],
      },
      {
        icon: Trophy,
        title: 'Song Wars',
        bullets: [
          'Song Wars panel can dock into the Now Playing sidebar or pop out into its own window.',
          'Bracket UI overhaul — clearer title bar, bracket view, and fixed action buttons.',
          'Skip now actually calls into the bracket engine instead of getting silently dropped.',
        ],
      },
      {
        icon: Minimize2,
        title: 'Minimize to Tray',
        bullets: [
          'Closing the window minimizes to the system tray instead of quitting.',
          'Discord Rich Presence now reports an idle state, with idle-time stats tracked alongside listening history.',
        ],
      },
      {
        icon: Palette,
        title: 'Visualizer Color',
        bullets: [
          'Visualizer colors now follow the active app theme instead of a fixed palette.',
        ],
      },
      {
        icon: Users,
        title: 'Shared Play v2',
        bullets: [
          'Shared Play sessions rewritten around short, SHA-256-derived 6-character room codes.',
          'New channels/packages model for session state, presence, and reactions.',
          'Redesigned web player with an immersive dark layout for browser listeners.',
        ],
      },
    ],
  },
  {
    version: '5.0.4',
    label: 'Cross-platform',
    date: 'Previous',
    summary: 'Spectralis goes cross-platform. Linux ships as a native x86_64 AppImage. Velopack replaces Squirrel for in-process auto-updates with delta patching, and Avalonia UI powers a shared rendering layer on Windows and Linux.',
    metrics: ['Linux AppImage', 'Velopack updater', 'Delta patches'],
    groups: [
      {
        icon: Terminal,
        title: 'Linux Support',
        bullets: [
          'Native x86_64 AppImage — self-contained, no package manager or admin rights required.',
          'Full feature parity with Windows: all visualizers, synced lyrics, Discord RPC, and OBS overlay server.',
          '.desktop file and MIME type registration for native system integration.',
          'Avalonia UI replaces WinForms, enabling a single shared rendering layer on both platforms.',
        ],
      },
      {
        icon: RefreshCw,
        title: 'Velopack Auto-Updater',
        bullets: [
          'In-process update checks replace the legacy Squirrel installer.',
          'Delta packages — only changed bytes are downloaded on each update.',
          'Channel-aware release feed on the CDN (win-x64; linux-x64 feed coming next).',
          'Updates apply silently on next launch — no UAC prompt, no manual installer.',
        ],
      },
    ],
  },
  {
    version: '4.6.1',
    label: 'UI polish',
    date: 'Previous',
    summary: 'A focused comfort pass for the 4.6 UI: clearer workspace navigation, a more useful visualizer toolbar, and menus grouped around how people actually use the app.',
    metrics: ['Workflow fixes', 'View switching', 'Toolbar polish'],
    groups: [
      {
        icon: MonitorPlay,
        title: 'Main Window Flow',
        bullets: [
          'Visualizer selection, previous/next, peak hold, response, auto-cycle, and lyric inspection now live in one coherent toolbar.',
          'Compact visualizer controls have clearer labels and hover hints.',
          'Peak hold and visualizer response changes now persist immediately from the toolbar.',
        ],
      },
      {
        icon: Layers,
        title: 'Library and Playlists',
        bullets: [
          'Library Browser and Playlists are explicit workspace views instead of ambiguous show/hide toggles.',
          'The Library menu now includes a checked Now Playing view for getting back to playback.',
          'Library and Playlist views span the full content area and stay visible while browsing.',
          'Saving the queue as a playlist opens the Playlists workspace directly.',
        ],
      },
      {
        icon: Sparkles,
        title: 'Menu Cleanup',
        bullets: [
          'Library actions are grouped around browsing, playlists, analysis, stats, scrobbling, and folders.',
          'Tools are grouped into Audio, Creator, and Live instead of one flat mixed list.',
          'Embedded content and YouTube video no longer jump in front of Library or Playlists.',
        ],
      },
    ],
  },
  {
    version: '4.6.0',
    label: 'Overhaul release',
    date: 'Previous',
    summary: 'A full creator-and-listener upgrade: library, playlists, metadata editing, scrobbling, analysis, effects, karaoke, and custom visualizer scripting.',
    metrics: ['8 feature drops', 'Library-first workflow', 'Creator tooling'],
    groups: [
      {
        icon: Music,
        title: 'Library Management',
        bullets: [
          'Persistent local music library with watched folders and background scanning.',
          'Automatic updates when tracks are added, removed, or renamed.',
          'Search and browsing by tracks, artists, albums, and genres.',
          'Play counts and last-played timestamps.',
        ],
      },
      {
        icon: FileCode2,
        title: 'Tag Editor',
        bullets: [
          'Edit core metadata, BPM, comments, composer, and cover art.',
          'Batch edit multiple files.',
          'Fetch metadata from MusicBrainz and cover art from Cover Art Archive.',
          'Creates a backup before the first metadata write.',
        ],
      },
      {
        icon: Layers,
        title: 'Playlists',
        bullets: [
          'Persistent playlists with browser and editor screens.',
          'Smart playlists powered by library metadata.',
          'M3U and M3U8 import/open support.',
          'Save the current queue as a playlist.',
        ],
      },
      {
        icon: Radio,
        title: 'Scrobbling and Stats',
        bullets: [
          'Last.fm and ListenBrainz scrobbling.',
          'Now-playing updates for connected services.',
          'Offline scrobble queue with retry.',
          'Local listening history and My Listening stats.',
        ],
      },
      {
        icon: BarChart3,
        title: 'Beat Grid and Analysis',
        bullets: [
          'BPM and key analysis for library tracks.',
          'Background library analysis with progress feedback.',
          'On-demand analysis from the library browser.',
          'Metronome seeded from the current track BPM.',
        ],
      },
      {
        icon: Activity,
        title: 'Audio Effects',
        bullets: [
          'Real-time effects chain.',
          '10-band EQ, compressor, reverb, and vocal remover.',
          'Add, remove, reorder, enable, and tune effects in-app.',
          'Playback pipeline rebuilds as settings change.',
        ],
      },
      {
        icon: Mic2,
        title: 'Karaoke Mode',
        bullets: [
          'Full-screen lyric display with previous/current/next context.',
          'Line and syllable-style lyric fill animation.',
          'Playback control from the karaoke window.',
          'Vocal blend slider backed by the effects chain.',
        ],
      },
      {
        icon: Sparkles,
        title: 'Scripted Visualizers',
        bullets: [
          'JavaScript-based visualizer scripting runtime.',
          'Local scripted visualizer storage.',
          'Script manager and editor surface.',
          'Canvas-like drawing context for custom visuals.',
        ],
      },
    ],
  },
  {
    version: '4.5.x',
    label: 'Song Wars',
    date: 'Earlier',
    summary: 'Introduced tournament mode foundations for live song battles.',
    metrics: ['Tournament core', 'Judge voting', 'Smoke tests'],
    groups: [
      {
        icon: GitFork,
        title: 'Song Wars Tournament System',
        bullets: [
          'Local tournament setup with submissions, judges, brackets, and match state.',
          'Double-elimination logic with winners bracket, losers bracket, grand finals, skips, byes, and replays.',
          'Vote tally rules for 1-5 judges with pass, fail, eliminated, tie-skip, and no-majority outcomes.',
          'Tournament persistence and smoke tests.',
        ],
      },
    ],
  },
  {
    version: '4.4.x',
    label: 'Lyrics engine',
    date: 'Earlier',
    summary: 'Expanded lyrics, metadata-driven playback, and the Song Wars plan.',
    metrics: ['Lyric Inspector', 'Glow pack', 'Engine work'],
    groups: [
      {
        icon: Mic2,
        title: 'Lyrics and Playback Engines',
        bullets: [
          'Richer lyrics and visualizer support for track-driven experiences.',
          'Lyric Inspector surface.',
          'Multiple-engine playback and visualizer handling improvements.',
        ],
      },
    ],
  },
  {
    version: '4.3.x',
    label: 'Streaming tools',
    date: 'Earlier',
    summary: 'Shared Play, OBS, extension, legal, and integration polish.',
    metrics: ['Shared Play polish', 'OBS editor', 'Extension'],
    groups: [
      {
        icon: MonitorPlay,
        title: 'Shared Play and OBS',
        bullets: [
          'Improved Shared Play rooms, browser player layout, cache behavior, and network handling.',
          'Expanded OBS overlay tooling with editor/canvas work and layout smoke tests.',
          'Added Chromium extension files and external-open IPC support.',
        ],
      },
    ],
  },
  {
    version: '4.2.x',
    label: 'Album worlds',
    date: 'Earlier',
    summary: 'Album worlds, MIDI playback, docs, and updater improvements.',
    metrics: ['Album worlds', 'MIDI/KAR', 'Docs'],
    groups: [
      {
        icon: Package,
        title: 'Album Worlds and MIDI',
        bullets: [
          'Signed .spectral album world support.',
          'Album capsule reading, runtime state, session storage, cache storage, and fallback UI.',
          'MIDI and KAR playback with bundled SoundFont assets.',
        ],
      },
    ],
  },
  {
    version: '4.0.x - 4.1.x',
    label: 'Shared backend',
    date: 'Earlier',
    summary: 'Backend-backed Shared Play and Zero capsule hardening.',
    metrics: ['Rust backend', 'Zero hardening', 'Web player'],
    groups: [
      {
        icon: Shield,
        title: 'Shared Play Backend and Zero Hardening',
        bullets: [
          'Rust Shared Play backend, Railway config, and browser player.',
          'Shared Play cache, session, package, presence, queue, and reaction support.',
          'Zero capsule lifecycle fixes, inactive handling, and animation-frame cleanup.',
        ],
      },
    ],
  },
  {
    version: '3.x',
    label: 'Capsules and OBS',
    date: 'Earlier',
    summary: 'Capsules, OBS overlays, video export, lyrics, and reactive content.',
    metrics: ['Capsules', 'OBS overlay', 'Video export'],
    groups: [
      {
        icon: Package,
        title: 'Capsules, OBS, and Export',
        bullets: [
          'Signed .spectralis single-track capsules with creator trust checks.',
          'Local OBS overlay server, layout presets, and high-DPI spectrum canvas.',
          'Video export support, richer synced lyrics, Spotify lyrics, and reactive content packs.',
        ],
      },
    ],
  },
  {
    version: '1.x - 2.x',
    label: 'Foundation',
    date: 'Earlier',
    summary: 'The first player, visualizers, metadata, lyrics, external sources, and Shared Play.',
    metrics: ['Initial player', 'Visualizers', 'External sources'],
    groups: [
      {
        icon: Waves,
        title: 'Player Foundation',
        bullets: [
          'Windows desktop audio player with metadata extraction and synced .lrc lyrics.',
          'Early visualizers, themes, embedded WASM/HTML experiences, and release packaging.',
          'Shared Play foundations, queue improvements, Spotify groundwork, and external URL handling.',
        ],
      },
    ],
  },
]

// OBS preset definitions (from ObsOverlayHtml.cs source)
const OBS_PRESETS = [
  {
    id: 'compact',
    label: 'compact',
    desc: 'Corner card with art, track info, and mini spectrum',
    render: () => (
      <div className="obs-mock obs-mock--compact">
        <div className="obs-mock__bg" />
        <div className="obs-mock__card obs-mock__card--bl">
          <div className="obs-mock__np">
            <div className="obs-mock__art" />
            <div className="obs-mock__meta">
              <div className="obs-mock__title" />
              <div className="obs-mock__artist" />
              <div className="obs-mock__prog"><div className="obs-mock__prog-fill" style={{ width: '42%' }} /></div>
            </div>
          </div>
          <div className="obs-mock__minibars">
            {[40,70,55,90,65,80,45,75,60,85,50,70,40,90,60,45,80,55].map((h,i) => (
              <div key={i} className="obs-mock__minibar" style={{ height: `${h}%` }} />
            ))}
          </div>
        </div>
      </div>
    ),
  },
  {
    id: 'lyrics-lower-third',
    label: 'lyrics-lower-third',
    desc: 'Full-width gradient with lyric text at the bottom',
    render: () => (
      <div className="obs-mock obs-mock--lyrics">
        <div className="obs-mock__bg" />
        <div className="obs-mock__lower-third">
          <div className="obs-mock__np obs-mock__np--centered">
            <div className="obs-mock__art obs-mock__art--sm" />
            <div className="obs-mock__meta">
              <div className="obs-mock__title obs-mock__title--sm" />
              <div className="obs-mock__artist obs-mock__artist--sm" />
            </div>
          </div>
          <div className="obs-mock__lyric-cur" />
          <div className="obs-mock__lyric-next" />
        </div>
      </div>
    ),
  },
  {
    id: 'full-visualizer',
    label: 'full-visualizer',
    desc: 'Spectrum bars spanning the full overlay with floating track info',
    render: () => (
      <div className="obs-mock obs-mock--fullviz">
        <div className="obs-mock__bg" />
        <div className="obs-mock__fullbars">
          {Array.from({ length: 28 }, (_, i) => {
            const h = 20 + Math.abs(Math.sin(i * 0.7)) * 70 + Math.abs(Math.cos(i * 1.3)) * 15
            return <div key={i} className="obs-mock__fullbar" style={{ height: `${h}%` }} />
          })}
        </div>
        <div className="obs-mock__card obs-mock__card--tl obs-mock__card--small">
          <div className="obs-mock__np">
            <div className="obs-mock__art obs-mock__art--xs" />
            <div className="obs-mock__meta">
              <div className="obs-mock__title obs-mock__title--xs" />
              <div className="obs-mock__artist obs-mock__artist--xs" />
            </div>
          </div>
        </div>
      </div>
    ),
  },
  {
    id: 'queue-sidebar',
    label: 'queue-sidebar',
    desc: 'Top-right sidebar showing the current queue',
    render: () => (
      <div className="obs-mock obs-mock--queue">
        <div className="obs-mock__bg" />
        <div className="obs-mock__card obs-mock__card--tr obs-mock__card--sidebar">
          <div className="obs-mock__np">
            <div className="obs-mock__art obs-mock__art--xs" />
            <div className="obs-mock__meta">
              <div className="obs-mock__title obs-mock__title--xs" />
              <div className="obs-mock__artist obs-mock__artist--xs" />
            </div>
          </div>
          <div className="obs-mock__divider" />
          {[0.7, 0.5, 0.5, 0.4, 0.4].map((w, i) => (
            <div key={i} className={`obs-mock__qrow${i === 0 ? ' obs-mock__qrow--active' : ''}`}>
              <div className="obs-mock__qdot" />
              <div className="obs-mock__qline" style={{ width: `${w * 100}%` }} />
            </div>
          ))}
        </div>
      </div>
    ),
  },
  {
    id: 'album-card',
    label: 'album-card',
    desc: 'Large album art card with track info and mini spectrum',
    render: () => (
      <div className="obs-mock obs-mock--album">
        <div className="obs-mock__bg" />
        <div className="obs-mock__card obs-mock__card--tl obs-mock__card--albumcard">
          <div className="obs-mock__np obs-mock__np--albumcard">
            <div className="obs-mock__art obs-mock__art--lg" />
            <div className="obs-mock__meta">
              <div className="obs-mock__title obs-mock__title--lg" />
              <div className="obs-mock__artist obs-mock__artist--lg" />
              <div className="obs-mock__prog" style={{ marginTop: 4 }}><div className="obs-mock__prog-fill" style={{ width: '38%' }} /></div>
            </div>
          </div>
          <div className="obs-mock__minibars obs-mock__minibars--wide">
            {[35,60,48,82,60,75,42,70,55,80,45,65,38,84,55,42,75,50,62,44,78,52].map((h,i) => (
              <div key={i} className="obs-mock__minibar" style={{ height: `${h}%` }} />
            ))}
          </div>
        </div>
      </div>
    ),
  },
  {
    id: 'stage-banner',
    label: 'stage-banner',
    desc: 'Centered top banner with art, track info, lyrics, and spectrum',
    render: () => (
      <div className="obs-mock obs-mock--stage">
        <div className="obs-mock__bg" />
        <div className="obs-mock__banner">
          <div className="obs-mock__np obs-mock__np--centered">
            <div className="obs-mock__art obs-mock__art--sm" />
            <div className="obs-mock__meta">
              <div className="obs-mock__title obs-mock__title--lg" />
              <div className="obs-mock__artist obs-mock__artist--sm" />
            </div>
          </div>
          <div className="obs-mock__minibars obs-mock__minibars--wide" style={{ height: 18 }}>
            {[40,70,55,90,65,80,45,75,60,85,50,70,40,90,60,45,80,55,70,55,90,65].map((h,i) => (
              <div key={i} className="obs-mock__minibar" style={{ height: `${h}%` }} />
            ))}
          </div>
          <div className="obs-mock__lyric-cur obs-mock__lyric-cur--banner" />
        </div>
      </div>
    ),
  },
]

// ── navbar ─────────────────────────────────────────────────────────────────────

function Navbar({ page, navigate }) {
  const [scrolled, setScrolled] = useState(false)
  useEffect(() => {
    const fn = () => setScrolled(window.scrollY > 40)
    window.addEventListener('scroll', fn, { passive: true })
    return () => window.removeEventListener('scroll', fn)
  }, [])

  const goSection = (e, id) => {
    if (page === 'downloads') {
      e.preventDefault()
      navigate('home')
      setTimeout(() => {
        const el = document.getElementById(id)
        if (el) el.scrollIntoView({ behavior: 'smooth' })
      }, 60)
    }
  }

  return (
    <nav className={`navbar ${scrolled ? 'navbar--scrolled' : ''}`}>
      <a
        href="#"
        className="navbar__logo"
        onClick={(e) => { e.preventDefault(); navigate('home') }}
      >
        <img src="/icon.png" alt="Spectralis" className="navbar__icon" />
        <span>Spectralis</span>
      </a>
      <div className="navbar__links">
        {page === 'downloads' ? (
          <button className="navbar__back" onClick={() => navigate('home')}>
            ← Home
          </button>
        ) : (
          <>
            <a href="#features"     onClick={(e) => goSection(e, 'features')}>Features</a>
            <a href="#changelog"    onClick={(e) => goSection(e, 'changelog')}>Changelog</a>
            <a href="#visualizers"  onClick={(e) => goSection(e, 'visualizers')}>Visualizers</a>
            <a href="#obs"          onClick={(e) => goSection(e, 'obs')}>OBS</a>
            <a href="#capsule"      onClick={(e) => goSection(e, 'capsule')}>Capsule</a>
          </>
        )}
        <a
          href="#"
          className={`navbar__dl-link${page === 'downloads' ? ' navbar__dl-link--active' : ''}`}
          onClick={(e) => { e.preventDefault(); navigate('downloads') }}
        >
          <Download size={13} />
          Downloads
        </a>
        <a href="https://github.com/dareto-dream/audioplayer" target="_blank" className="navbar__gh">
          <GitFork size={14} />
          GitHub
          <ArrowUpRight size={12} className="navbar__gh-arrow" />
        </a>
      </div>
    </nav>
  )
}

// ── hero ───────────────────────────────────────────────────────────────────────

function Hero({ navigate }) {
  return (
    <section className="hero" id="hero">
      <div className="hero__rule" />
      <div className="hero__inner">
        <div className="hero__logo-block">
          <img src="/icon.png" alt="Spectralis" className="hero__logo" />
          <div className="hero__tag">
            <span className="hero__tag-dot" />
            Windows · Linux · Free
          </div>
        </div>
        <div className="hero__headline-block">
          <h1 className="hero__title">
            The next gen<br />
            of visual music<br />
            experience<br />
            is here.
          </h1>
        </div>
        <div className="hero__right-block">
          <p className="hero__sub">
            Spectralis is an audio player built for listeners and artists
            who demand more — cinematic visualizers, synced lyrics, signed capsule
            releases, and live OBS overlays. Now on Windows and Linux.
          </p>
          <div className="hero__actions">
            <a
              href="#"
              className="btn btn--primary"
              onClick={(e) => { e.preventDefault(); navigate('downloads') }}
            >
              <Download size={15} />
              Download free
            </a>
            <a href="#features" className="btn btn--ghost">
              See features
              <ArrowRight size={14} />
            </a>
          </div>
          <dl className="hero__specs">
            <div><dt>Platform</dt><dd>Windows 10/11 · Linux x86_64</dd></div>
            <div><dt>Runtime</dt><dd>.NET 8 · WebView2 / Avalonia</dd></div>
            <div><dt>License</dt><dd>Free</dd></div>
          </dl>
        </div>
      </div>
      <div className="hero__counts">
        {[
          ['11', 'Built-in visualizers'],
          ['12+', 'Audio formats'],
          ['11', 'OBS presets'],
          ['Ed25519', 'Capsule signing'],
          ['60', 'FPS rendering'],
        ].map(([n, l]) => (
          <div key={l} className="hero__count">
            <span className="hero__count-n">{n}</span>
            <span className="hero__count-l">{l}</span>
          </div>
        ))}
      </div>
    </section>
  )
}

// ── features ───────────────────────────────────────────────────────────────────

function Features() {
  return (
    <section className="features section" id="features">
      <div className="section__head">
        <span className="section__label">Features</span>
        <h2 className="section__title">Everything you need.</h2>
      </div>
      <div className="feat-list">
        {FEATURES.map(({ num, icon: Icon, title, body }) => (
          <div key={num} className="feat-row">
            <span className="feat-row__num">{num}</span>
            <h3 className="feat-row__title">
              <Icon size={15} className="feat-row__icon" />
              {title}
            </h3>
            <p className="feat-row__body">{body}</p>
          </div>
        ))}
      </div>
    </section>
  )
}

// ── changelog ─────────────────────────────────────────────────────────────────

function ChangelogSection() {
  const [activeVersion, setActiveVersion] = useState(CHANGELOG_RELEASES[0].version)
  const activeRelease = CHANGELOG_RELEASES.find(r => r.version === activeVersion) ?? CHANGELOG_RELEASES[0]
  const isLatest = activeRelease.version === CHANGELOG_RELEASES[0].version

  return (
    <section className="changelog section" id="changelog">
      <div className="section__head">
        <span className="section__label">Changelog</span>
        <h2 className="section__title">What changed,<br />by release.</h2>
      </div>

      <div className="changelog-shell">
        <nav className="changelog-rail" aria-label="Release versions">
          <div className="changelog-rail__line" />
          {CHANGELOG_RELEASES.map((release, i) => (
            <button
              key={release.version}
              type="button"
              className={`changelog-rail__item${release.version === activeVersion ? ' active' : ''}`}
              onClick={() => setActiveVersion(release.version)}
            >
              <span className="changelog-rail__dot" />
              <span className="changelog-rail__version">
                {release.version}
                {i === 0 && <span className="changelog-rail__badge">NEW</span>}
              </span>
              <span className="changelog-rail__label">{release.label}</span>
            </button>
          ))}
        </nav>

        <article className="changelog-release">
          {isLatest && (
            <div className="changelog-release__latest-bar">
              <span className="changelog-release__latest-dot" />
              Latest release
            </div>
          )}

          <div className="changelog-release__top">
            <div>
              <p className="changelog-release__eyebrow">{activeRelease.date}</p>
              <h3 className="changelog-release__title">{activeRelease.version}</h3>
            </div>
            <div className="changelog-release__metrics">
              {activeRelease.metrics.map(metric => (
                <span key={metric}>{metric}</span>
              ))}
            </div>
          </div>

          <p className="changelog-release__summary">{activeRelease.summary}</p>

          <div className="changelog-features">
            {activeRelease.groups.map(({ icon: Icon, title, bullets }) => (
              <section key={title} className="changelog-feature">
                <div className="changelog-feature__head">
                  <span className="changelog-feature__icon"><Icon size={16} /></span>
                  <h4>{title}</h4>
                </div>
                <ul>
                  {bullets.map(item => <li key={item}>{item}</li>)}
                </ul>
              </section>
            ))}
          </div>
        </article>
      </div>
    </section>
  )
}

// ── visualizers ────────────────────────────────────────────────────────────────

function Visualizers() {
  return (
    <section className="visualizers section" id="visualizers">
      <div className="section__head">
        <span className="section__label">Visual engine</span>
        <h2 className="section__title">11 visualizers, live.</h2>
      </div>
      <p className="viz-note">Interactive demo — randomized audio data. Click any tab.</p>
      <VisualizerDemo />
    </section>
  )
}

// ── OBS ────────────────────────────────────────────────────────────────────────

function OBSSection() {
  return (
    <section className="obs section" id="obs">
      <div className="section__head">
        <span className="section__label">OBS integration</span>
        <h2 className="section__title">Stream-ready,<br />out of the box.</h2>
      </div>
      <div className="obs-inner">
        <div className="obs-copy">
          <p className="obs-body">
            Spectralis serves a browser overlay at <code>localhost:5128</code> via SSE.
            Eleven layout presets via <code>?preset=</code>. State pushes the instant
            the track changes — no polling, no page reloads, no lag.
          </p>
          <ul className="obs-features">
            <li>High-DPI spectrum canvas</li>
            <li>Artwork cache-busting</li>
            <li>Current + next lyric lines</li>
            <li>Queue display</li>
            <li>SSE state push at 100 ms cadence</li>
          </ul>
        </div>
        <div className="obs-presets-grid">
          {OBS_PRESETS.map(({ id, label, desc, render }) => (
            <div key={id} className="obs-card">
              {render()}
              <div className="obs-card__info">
                <code className="obs-card__label">{label}</code>
                <p className="obs-card__desc">{desc}</p>
              </div>
            </div>
          ))}
        </div>
      </div>
    </section>
  )
}

// ── capsule ────────────────────────────────────────────────────────────────────

function CapsuleSection() {
  return (
    <section className="capsule section" id="capsule">
      <div className="section__head">
        <span className="section__label">Artist format</span>
        <h2 className="section__title">Signed. Sealed.<br />Self-contained.</h2>
      </div>
      <div className="cap-inner">
        <div className="cap-left">
          <p className="cap-body">
            A <code>.spectralis</code> file is a binary capsule — Ed25519-signed,
            self-verifying, self-contained. Audio, art, lyrics, reactive timeline
            events, and sandboxed WASM visualizers live in one file that creators
            sign and listeners trust.
          </p>
          <div className="cap-card">
            <div className="cap-card__bar">
              <span className="cap-card__dot" style={{ background: '#ef4444' }} />
              <span className="cap-card__dot" style={{ background: '#eab308' }} />
              <span className="cap-card__dot" style={{ background: '#22c55e' }} />
              <code className="cap-card__name">release.spectralis</code>
            </div>
            <pre className="cap-card__code">{`[4]  Magic .............. SPCC
[4]  Format version ..... 3
[32] Ed25519 public key
[64] Ed25519 signature
     ↓ ZIP payload
     manifest.json
     audio/track.flac
     reactive.json
     assets/images/cover.png
     assets/data/lyrics.lrc`}</pre>
          </div>
        </div>
        <div className="cap-right">
          <div className="cap-formats">
            <p className="cap-formats__label">Supported formats</p>
            <div className="cap-formats__list">
              {FORMAT_LIST.map(f => <span key={f} className="cap-formats__pill">{f}</span>)}
            </div>
          </div>
          <div className="cap-trust">
            <Shield size={15} className="cap-trust__icon" />
            <div>
              <p className="cap-trust__title">Creator trust system</p>
              <p className="cap-trust__body">Unknown keys trigger a one-time trust prompt. Revoked keys always rejected. Capabilities intersection-enforced against cdn.deltavdevs.com.</p>
            </div>
          </div>
          <div className="cap-trust">
            <Zap size={15} className="cap-trust__icon" />
            <div>
              <p className="cap-trust__title">Reactive timeline</p>
              <p className="cap-trust__body">Section tracking, smooth parameter transitions, and easing curves synchronized frame-by-frame to playback position.</p>
            </div>
          </div>
        </div>
      </div>
    </section>
  )
}

// ── CTA ────────────────────────────────────────────────────────────────────────

function CTASection({ navigate }) {
  return (
    <section className="cta section" id="download">
      <div className="cta-inner">
        <img src="/icon.png" alt="Spectralis" className="cta-logo" />
        <div className="cta-text">
          <h2 className="cta-title">Download free.<br />Hear it differently.</h2>
          <p className="cta-sub">Windows 10/11 · Linux x86_64 · No sign-in · Free</p>
          <div className="cta-actions">
            <a
              href="#"
              className="btn btn--primary btn--lg"
              onClick={(e) => { e.preventDefault(); navigate('downloads') }}
            >
              <Download size={16} />
              Get Spectralis
            </a>
            <a href="https://github.com/dareto-dream/audioplayer" target="_blank" className="btn btn--ghost btn--lg">
              <GitFork size={16} />
              View on GitHub
            </a>
          </div>
        </div>
      </div>
    </section>
  )
}

// ── downloads page ─────────────────────────────────────────────────────────────

function DownloadsPage({ navigate }) {
  return (
    <main className="dl-page">
      <div className="dl-hero">
        <div className="dl-hero__inner">
          <div className="dl-hero__head">
            <div className="dl-hero__version-row">
              <span className="dl-hero__v">v5.2.0</span>
              <span className="dl-hero__v-badge">Latest</span>
            </div>
            <h1 className="dl-hero__title">Get Spectralis</h1>
            <p className="dl-hero__sub">Free · No sign-in required · Self-contained</p>
          </div>
          <div className="dl-hero__meta">
            <div className="dl-hero__stat">
              <span className="dl-hero__stat-n">Windows</span>
              <span className="dl-hero__stat-l">Velopack installer</span>
            </div>
            <div className="dl-hero__stat">
              <span className="dl-hero__stat-n">Linux</span>
              <span className="dl-hero__stat-l">AppImage · New in v5</span>
            </div>
            <div className="dl-hero__stat">
              <span className="dl-hero__stat-n">Free</span>
              <span className="dl-hero__stat-l">No license key</span>
            </div>
          </div>
        </div>
      </div>

      <div className="dl-platforms">

        {/* Windows */}
        <div className="dl-card dl-card--windows">
          <div className="dl-card__header">
            <div className="dl-card__icon">
              <Download size={18} />
            </div>
            <div className="dl-card__header-text">
              <h2 className="dl-card__title">Windows</h2>
              <p className="dl-card__platform-sub">10 / 11 · x64</p>
            </div>
            <span className="dl-card__badge">Recommended</span>
          </div>

          <div className="dl-card__file">
            <span className="dl-card__filename">Spectralis-win-x64-Setup.exe</span>
            <span className="dl-card__filetype">Velopack installer</span>
          </div>

          <a
            href="https://cdn.deltavdevs.com/spectralis/Spectralis-win-x64-Setup.exe"
            className="btn btn--primary btn--lg dl-card__btn"
          >
            <Download size={16} />
            Download for Windows
          </a>

          <div className="dl-card__notes">
            <div className="dl-note">
              <span className="dl-note__label">Auto-updates</span>
              <p className="dl-note__body">Velopack checks for updates on launch and applies delta patches silently — no re-installer needed.</p>
            </div>
            <div className="dl-note">
              <span className="dl-note__label">Install path</span>
              <p className="dl-note__body">Installs to <code>%LocalAppData%\Spectralis</code> — no admin rights required.</p>
            </div>
          </div>

          <div className="dl-card__steps">
            <p className="dl-steps__label">To install</p>
            <ol className="dl-steps">
              <li>Run <code>Setup.exe</code></li>
              <li>Spectralis launches automatically after install</li>
              <li>Future updates apply on next launch</li>
            </ol>
          </div>
        </div>

        {/* Linux */}
        <div className="dl-card dl-card--linux">
          <div className="dl-card__header">
            <div className="dl-card__icon">
              <Terminal size={18} />
            </div>
            <div className="dl-card__header-text">
              <h2 className="dl-card__title">Linux</h2>
              <p className="dl-card__platform-sub">x86_64 · AppImage</p>
            </div>
            <span className="dl-card__badge dl-card__badge--new">New in v5</span>
          </div>

          <div className="dl-card__file">
            <span className="dl-card__filename">Spectralis-linux-x64.AppImage</span>
            <span className="dl-card__filetype">Self-contained · No install required</span>
          </div>

          <a
            href="https://cdn.deltavdevs.com/spectralis/Spectralis-linux-x64.AppImage"
            className="btn btn--primary btn--lg dl-card__btn"
          >
            <Download size={16} />
            Download AppImage
          </a>

          <div className="dl-card__notes">
            <div className="dl-note">
              <span className="dl-note__label">Self-contained</span>
              <p className="dl-note__body">Everything is bundled — no runtime dependencies, no package manager, no root required.</p>
            </div>
            <div className="dl-note">
              <span className="dl-note__label">FUSE</span>
              <p className="dl-note__body">Most distros include FUSE. If not, run with <code>--appimage-extract-and-run</code> as a fallback.</p>
            </div>
          </div>

          <div className="dl-card__steps">
            <p className="dl-steps__label">To run</p>
            <ol className="dl-steps">
              <li><code>chmod +x Spectralis-linux-x64.AppImage</code></li>
              <li>Double-click or run from terminal</li>
              <li>Optional: use AppImageLauncher for system integration</li>
            </ol>
          </div>
        </div>
      </div>

      <div className="dl-requirements">
        <h2 className="dl-requirements__title">System requirements</h2>
        <div className="dl-req-grid">
          <div className="dl-req-col">
            <h3>Windows</h3>
            <table className="dl-req-table">
              <tbody>
                <tr><td>OS</td><td>Windows 10 or 11 (64-bit)</td></tr>
                <tr><td>Runtime</td><td>.NET 8 (bundled)</td></tr>
                <tr><td>Display</td><td>WebView2 (bundled)</td></tr>
                <tr><td>Arch</td><td>x64</td></tr>
              </tbody>
            </table>
          </div>
          <div className="dl-req-col">
            <h3>Linux</h3>
            <table className="dl-req-table">
              <tbody>
                <tr><td>OS</td><td>Any modern x86_64 Linux distro</td></tr>
                <tr><td>libc</td><td>glibc 2.17 or newer</td></tr>
                <tr><td>FUSE</td><td>libfuse2 (optional)</td></tr>
                <tr><td>Arch</td><td>x86_64</td></tr>
              </tbody>
            </table>
          </div>
        </div>
      </div>

      <div className="dl-footer-links">
        <a
          href="#"
          className="btn btn--ghost"
          onClick={(e) => { e.preventDefault(); navigate('home') }}
        >
          ← Back to home
        </a>
        <a
          href="https://github.com/dareto-dream/audioplayer"
          target="_blank"
          className="btn btn--ghost"
        >
          <GitFork size={14} />
          View source on GitHub
          <ArrowUpRight size={13} />
        </a>
      </div>
    </main>
  )
}

// ── footer ─────────────────────────────────────────────────────────────────────

function Footer({ navigate }) {
  return (
    <footer className="footer">
      <div className="footer__inner">
        <div className="footer__brand">
          <img src="/icon.png" alt="Spectralis" className="footer__logo" />
          <div>
            <p className="footer__name">Spectralis</p>
            <p className="footer__sub">The next gen of visual music experience.</p>
          </div>
        </div>
        <div className="footer__mid">
          <p className="footer__by">
            Made by{' '}
            <a href="https://deltavdevs.com" target="_blank" className="footer__dvlink">
              DeltaV Devs
            </a>
          </p>
          <p className="footer__deltawave">
            From the creators of{' '}
            <a href="https://deltavdevs.com" target="_blank" className="footer__dvlink">
              DeltaWave
            </a>
          </p>
        </div>
        <nav className="footer__nav">
          <a href="#features">Features</a>
          <a href="#changelog">Changelog</a>
          <a href="#visualizers">Visualizers</a>
          <a href="#obs">OBS</a>
          <a href="#capsule">Capsule</a>
          <a href="#" onClick={(e) => { e.preventDefault(); navigate('downloads') }}>Downloads</a>
          <a href="https://github.com/dareto-dream/audioplayer" target="_blank">GitHub ↗</a>
        </nav>
        <p className="footer__copy">© 2025 DeltaV Devs</p>
      </div>
    </footer>
  )
}

// ── app ────────────────────────────────────────────────────────────────────────

export default function App() {
  const [page, setPage] = useState('home')

  const navigate = (p) => {
    setPage(p)
    window.scrollTo({ top: 0, behavior: 'instant' })
  }

  return (
    <>
      <Navbar page={page} navigate={navigate} />
      {page === 'downloads' ? (
        <DownloadsPage navigate={navigate} />
      ) : (
        <>
          <Hero navigate={navigate} />
          <Features />
          <ChangelogSection />
          <Visualizers />
          <OBSSection />
          <CapsuleSection />
          <CTASection navigate={navigate} />
        </>
      )}
      <Footer navigate={navigate} />
    </>
  )
}
