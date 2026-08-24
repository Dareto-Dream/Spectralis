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

// Accent colors pulled straight from AppThemeService.cs (AppThemeAccent primaries) —
// same set the desktop app themes itself with, not a made-up web palette.
export const APP_ACCENTS = {
  violet: '#a882f2',
  ocean: '#5ca3ff',
  rose: '#e78aa7',
  sunset: '#fc8444',
  cyan: '#44cade',
  gold: '#e8c03a',
}

// Real in-app captures of every built-in visualizer, Idle state.
// A few carry a `tint` (one of the app's real theme accents) so the grid
// doesn't read as one flat stock-photo wall.
export const VISUALIZER_SHOTS = [
  { src: '/screenshots/visualizers/radial-spectrum.jpg', title: 'Radial Spectrum' },
  { src: '/screenshots/visualizers/mirror-spectrum.jpg',  title: 'Mirror Spectrum', tint: APP_ACCENTS.violet },
  { src: '/screenshots/visualizers/waveform.jpg',         title: 'Waveform', tint: APP_ACCENTS.ocean },
  { src: '/screenshots/visualizers/spectrum-wave.jpg',    title: 'Spectrum Wave' },
  { src: '/screenshots/visualizers/spectrum-bars.jpg',    title: 'Spectrum' },
  { src: '/screenshots/visualizers/vu-meter.jpg',         title: 'VU Meter', tint: APP_ACCENTS.gold },
  { src: '/screenshots/visualizers/loudness-meter.jpg',   title: 'Loudness Meter' },
  { src: '/screenshots/visualizers/stereometer.jpg',      title: 'Stereometer' },
  { src: '/screenshots/visualizers/spectrogram.jpg',      title: 'Spectrogram', tint: APP_ACCENTS.sunset },
  { src: '/screenshots/visualizers/dancing-colors.jpg',   title: 'Dancing Colors', tint: APP_ACCENTS.rose },
  { src: '/screenshots/visualizers/3d-graph.jpg',         title: '3D Graph' },
  { src: '/screenshots/visualizers/3d-sphere.jpg',        title: '3D Sphere', tint: APP_ACCENTS.cyan },
  { src: '/screenshots/visualizers/album-cover.jpg',      title: 'Album Cover' },
]
