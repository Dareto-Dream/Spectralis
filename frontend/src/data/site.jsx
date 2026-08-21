import {
  Waves, Radio, Zap, Shield, FileCode2, Package, MonitorPlay,
  BarChart3, Activity, Layers, Sparkles, Music, Mic2,
  Terminal, RefreshCw, ListMusic, Globe, Crosshair, Dices,
  Trophy, Minimize2, Palette, Users, NotebookPen, Bug, GitFork,
} from 'lucide-react'

export const FEATURES = [
  { num: '01', icon: Waves,      title: 'Cinematic Visualizers',  body: '15 built-in animated visualizers at 60 FPS — radial spectrums, spinning disks, 3D spheres, oscilloscopes, and more. Each one reacts to every beat in real time.' },
  { num: '02', icon: Mic2,       title: 'Synced Lyrics',          body: 'Real-time lyric sync from .lrc sidecars, embedded LRC text, or structured lyric data baked into the file.' },
  { num: '03', icon: Package,    title: '.spectralis Capsules',    body: 'Ed25519-signed artist packages — audio, art, lyrics, a reactive timeline, and WASM visualizers — all in one cryptographically verified file.' },
  { num: '04', icon: MonitorPlay,title: 'OBS Overlay',            body: '11 layout presets served live at localhost:5128 via SSE. High-DPI spectrum canvas, artwork cache-busting. No polling, no page reloads.' },
  { num: '05', icon: Radio,      title: 'Discord Rich Presence',  body: 'Publish what you\'re hearing on Discord with a Spectralis download link and a Listen Together button via Shared Play.' },
  { num: '06', icon: Music,      title: 'Spotify Integration',    body: 'Link your account and Spectralis registers as a native Spotify device — OBS-ready, no extra audio routing, no separate process.' },
  { num: '07', icon: Zap,        title: 'Reactive Timeline',      body: 'Per-track .spectralis-reactive.json sidecars drive smooth parameter transitions synchronized frame-by-frame to playback position.' },
  { num: '08', icon: FileCode2,  title: 'Embedded Experiences',   body: 'WASM visualizers, HTML overlays, Markdown content, and synced video — all living directly inside the audio file.' },
  { num: '09', icon: Shield,     title: 'Creator Trust',          body: 'Unknown creator keys trigger a one-time trust prompt. Revoked keys are always rejected. Capabilities are intersection-enforced against the CDN.' },
]

export const FORMAT_LIST = ['MP3','FLAC','WAV','OGG','Opus','M4A','AAC','WMA','WebM','AIFF','MP4']

export const CHANGELOG_URL = 'https://cdn.deltavdevs.com/spectralis/changelog.json'

// changelog groups reference icons by name (CDN JSON can't carry components)
export const CHANGELOG_ICONS = {
  Waves, Radio, Zap, Shield, FileCode2, Package, MonitorPlay, BarChart3,
  Activity, Layers, Sparkles, Music, Mic2, Terminal, RefreshCw, ListMusic,
  Globe, Crosshair, Dices, Trophy, Minimize2, Palette, Users, NotebookPen,
  Bug, GitFork,
}

export const SCREENSHOTS = [
  { src: '/screenshots/mirror-spectrum.png', title: 'Mirror Spectrum', body: 'Live spectrum with synced lyrics panel.' },
  { src: '/screenshots/streamer-queue.png',  title: 'Streamer Queue',  body: 'Viewer submissions, priority skips, queue settings.' },
  { src: '/screenshots/randomizer.png',      title: 'Randomizer Tools', body: 'Weighted spin wheel and coin toss for stream decisions.' },
  { src: '/screenshots/song-wars.png',       title: 'Song Wars',       body: 'Live bracket voting between tracks.' },
]

// Real in-app captures of every built-in visualizer, Idle state.
export const VISUALIZER_SHOTS = [
  { src: '/screenshots/visualizers/radial-spectrum.jpg', title: 'Radial Spectrum' },
  { src: '/screenshots/visualizers/mirror-spectrum.jpg',  title: 'Mirror Spectrum' },
  { src: '/screenshots/visualizers/waveform.jpg',         title: 'Waveform' },
  { src: '/screenshots/visualizers/spectrum-wave.jpg',    title: 'Spectrum Wave' },
  { src: '/screenshots/visualizers/spectrum-bars.jpg',    title: 'Spectrum' },
  { src: '/screenshots/visualizers/vu-meter.jpg',         title: 'VU Meter' },
  { src: '/screenshots/visualizers/loudness-meter.jpg',   title: 'Loudness Meter' },
  { src: '/screenshots/visualizers/stereometer.jpg',      title: 'Stereometer' },
  { src: '/screenshots/visualizers/spectrogram.jpg',      title: 'Spectrogram' },
  { src: '/screenshots/visualizers/dancing-colors.jpg',   title: 'Dancing Colors' },
  { src: '/screenshots/visualizers/3d-graph.jpg',         title: '3D Graph' },
  { src: '/screenshots/visualizers/3d-sphere.jpg',        title: '3D Sphere' },
  { src: '/screenshots/visualizers/album-cover.jpg',      title: 'Album Cover' },
]

// OBS preset definitions (from ObsOverlayHtml.cs source)
export const OBS_PRESETS = [
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
