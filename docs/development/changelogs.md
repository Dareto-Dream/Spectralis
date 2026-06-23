# Spectralis Changelog

Public changelog for Spectralis. Older releases are grouped by version family where patch-level
release notes were not recorded separately.

## 4.6.1

### UI and Workflow Polish

- Reworked the main visualizer toolbar so visualizer selection, previous/next controls, peak hold,
  response sensitivity, auto-cycle, and lyric inspection live together in one coherent control strip.
- Restored visible previous/next visualizer controls and added tooltips for compact toolbar actions.
- Moved visualizer response and peak controls out of the hidden settings row and into the listening
  workflow.
- Persisted toolbar changes for peak hold and visualizer response immediately.

### Library and Playlist Views

- Replaced ambiguous "Show Library" and "Show Playlists" toggles with explicit workspace switching.
- Added a "Now Playing" library menu item so users have a clear path back from Library or Playlists.
- Added checked menu state for Now Playing, Library Browser, and Playlists.
- Made Library Browser and Playlists span the full content area instead of competing with the
  visualizer, lyrics, and queue columns.
- Prevented embedded content and YouTube video from jumping back in front while the user is browsing
  Library or Playlists.
- Saving the queue as a playlist now opens the Playlists workspace directly.

### Menus

- Reorganized Library menu actions around browsing, playlists, analysis, listening stats,
  scrobbling, and folders.
- Reorganized Tools into Audio, Creator, and Live groups so effects, karaoke, metronome, visualizer
  scripting, lyric timing, OBS, and Song Wars no longer appear as one flat list.

## 4.6.0

### Library Management

- Added a persistent local music library.
- Added watched music folders with background scanning.
- Added automatic updates when files are added, removed, or renamed.
- Added library search and filtering by tracks, artists, albums, and genres.
- Added play counts and last-played timestamps.
- Added library settings for folder management and auto-scan behavior.

### Tag Editor

- Added a built-in tag editor for local music files.
- Supports editing title, artist, album artist, album, track number, disc number, year, genre,
  comment, composer, BPM, and cover art.
- Added batch tag editing for multiple selected files.
- Added MusicBrainz lookup with Cover Art Archive support.
- Creates a `.bak` backup before the first metadata write to a file.
- Refreshes library metadata after saves and reloads the current track when its tags change.

### Playlists

- Added persistent playlists.
- Added playlist browser and editor screens.
- Added smart playlists driven by library metadata.
- Added M3U and M3U8 import/open support.
- Added "Save Queue as Playlist" from the File menu and queue context menu.
- Added playlist loading into the playback queue.

### Scrobbling and Listening Stats

- Added Last.fm scrobbling support.
- Added ListenBrainz scrobbling support.
- Added now-playing updates for supported scrobble services.
- Added an offline scrobble queue that retries later when submission fails.
- Added local listening history and a "My Listening" stats view.
- Added scrobbling settings for account credentials and service toggles.

### Beat Grid and Analysis

- Added BPM analysis for library tracks.
- Added key analysis for library tracks.
- Added background library analysis with progress feedback.
- Added on-demand BPM/key analysis from the library browser.
- Added current-track beat grid state for analyzed tracks.
- Added a metronome window seeded from the current track BPM when available.

### Audio Effects

- Added a real-time audio effects chain.
- Added 10-band EQ.
- Added compressor.
- Added reverb.
- Added vocal remover / vocal blend effect.
- Added an effects chain dialog for adding, removing, reordering, enabling, and tuning effects.
- Rebuilds the audio pipeline when effect settings change.

### Karaoke Mode

- Added karaoke mode.
- Added a full-screen lyric display with previous/current/next lyric context.
- Added left-to-right lyric fill animation for line and syllable-style lyric timing.
- Added playback control from the karaoke window.
- Added a vocal blend slider backed by the new effects chain.

### Scripted Visualizers

- Added a JavaScript-based visualizer scripting runtime.
- Added scripted visualizer definitions and local storage.
- Added a visualizer script manager and editor surface.
- Added a canvas-like drawing context for scripts.
- Added support for applying scripted visualizers as the active visualizer choice.

## 4.5.x

### Song Wars

- Added the first Song Wars tournament system.
- Added local tournament setup, submission models, judge models, bracket state, and match state.
- Added double-elimination bracket logic with winners bracket, losers bracket, grand finals, skips,
  byes, and replayed matches.
- Added vote tally rules for 1-5 judges.
- Added pass, fail, eliminated, tie-skip, and no-majority outcomes.
- Added tournament persistence and smoke tests.
- Fixed Song Wars issues found during the first implementation pass.

## 4.4.x

### Lyrics and Playback Engines

- Expanded lyrics and visualizer support for richer track-driven experiences.
- Added the Lyric Inspector.
- Improved metadata-driven playback and visualizer handling across multiple engines.

### Content and Planning

- Added the `glow` metadata experience.
- Added the Song Wars implementation plan.
- Continued visualizer and startup polish.

## 4.3.x

### Shared Play

- Improved Shared Play rooms, web player layout, caching, and network behavior.
- Added room polish for browser listeners.
- Improved backend routes and Shared Play state updates.
- Added better handling for poor network conditions.

### OBS Overlay

- Expanded OBS overlay tooling.
- Added overlay editor and canvas work.
- Added layout smoke tests.
- Improved overlay state and visualizer rendering behavior.

### Integrations

- Added Suno clip resolving.
- Added a Chromium browser extension and external-open IPC support.
- Improved Spotify integration and account-linking behavior.

### App and Content

- Added in-app legal document surfaces.
- Added privacy policy and terms of service.
- Updated the app icon.
- Added and updated metadata experiences including `chrome-hearts` and `edo`.
- Added album-world fixes and lyric timing improvements.

## 4.2.x

### Album Worlds

- Added signed `.spectral` album world support.
- Added album capsule reading, runtime state, session storage, cache storage, and fallback UI.
- Added sample album-world metadata.

### MIDI

- Added MIDI and KAR playback support.
- Added bundled SoundFont assets through MeltySynth.
- Wired MIDI playback into visualizers, settings, and video export.

### Documentation and Updates

- Created the main documentation tree for app standards, creator tools, formats, API contracts,
  CDN contracts, legal docs, and album worlds.
- Improved update notices and startup update handling.

## 4.0.x - 4.1.x

### Shared Play Backend

- Added the Rust Shared Play backend.
- Added Railway deployment configuration.
- Added the browser Shared Play player.
- Added Shared Play cache, session, package, presence, queue, and reaction support.

### Zero Capsule Hardening

- Improved the Zero capsule visualizer lifecycle.
- Added post-song inactive handling and animation-frame cleanup.
- Added a short grace window so pausing does not immediately fade the visualizer.
- Cleaned up Zero capsule manifest/data references.
- Reduced the risk of timing drift in Zero's character segments.

### Video and Metadata

- Continued video/export work.
- Added kinetic typography tooling.
- Added and refined metadata packs including `my-thoughts` and `zero`.

## 3.2.x

### Video Export

- Added video export support and renderer options.
- Improved YouTube iframe handling.
- Added clipboard fixes for media workflows.

### Lyrics and Reactive Content

- Added richer synced lyric handling.
- Added Spotify lyrics support.
- Added browser Shared Play lyric updates.
- Added the `posted` reactive/capsule experience.
- Fixed lyric panel visibility behavior.

### Update Reliability

- Improved Squirrel update behavior.
- Added update prompt and update screen fixes.
- Fixed packaging behavior around YouTube and `yt-dlp`.

### Content Packs

- Added and refined metadata experiences including `bassape`, `zero`, `its-bad`, `SOS`,
  `my-thoughts`, and related sample content.
- Added BandLab handling.
- Added album-cover and visualizer improvements.

## 3.0.x - 3.1.x

### Spectralis Capsules

- Added signed `.spectralis` single-track capsules.
- Added capsule reading, creator trust checks, capability declarations, and capsule opening paths.
- Added capsule drag-and-drop support.
- Added embedded assets and reactive metadata support for capsules.

### OBS Integration

- Added the local OBS overlay server.
- Added overlay state, overlay HTML, layout presets, and settings.
- Added high-DPI spectrum canvas rendering for overlays.

### External Playback

- Added proper YouTube handling.
- Added local `yt-dlp` support.
- Added release-readiness and packaging improvements.

## 2.x

### External Sources

- Added support for opening more external audio/video sources.
- Added YouTube and SoundCloud URL handling improvements.
- Added Suno read/resolve work.

### Spotify

- Added Spotify integration groundwork.
- Added Spotify Web Playback SDK support.
- Added follow-up Spotify fixes.

### Visualizers and Workflow

- Added more visualizers and Zero visualizer updates.
- Added OBS parsing improvements.
- Added clipboard management.
- Added general playback and startup quality-of-life improvements.

## 1.3.x - 1.4.x

### Shared Play

- Added the first Shared Play experience.
- Added Discord Shared Play links.
- Added browser web-share playback.
- Added session state, join requests, queue state, listener presence, and web player fixes.

### Queue and Presence

- Improved queue behavior.
- Added Discord Rich Presence queue activity.
- Added default-player registration improvements.

### Visualizers and Integrations

- Began redeemable and installed visualizer support.
- Added early Spotify API integration.
- Added false-positive and installer reputation guidance.

## 1.0.x - 1.2.x

### Initial Player

- Added the first Spectralis Windows desktop audio player.
- Added the playback engine, WinForms shell, modern controls, supported format registry, and
  startup project.
- Added metadata extraction for titles, artists, albums, and cover art.
- Added synced `.lrc` lyric loading and display.

### Visualizers and Themes

- Added the first spectrum visualizer.
- Added waveform, spinning disk, radial spectrum, oscilloscope, VU meter, 3D, and other early
  visualizer work.
- Added theme palettes, custom controls, and window chrome styling.
- Fixed early 3D visualizer behavior.

### Embedded Experiences and Releases

- Added embedded module support, including WASM and HTML content.
- Added initial metadata packs including `sorrow`, `vita-carnis`, and `stars-collide`.
- Added Squirrel release packaging, setup scripts, and update artifacts.
- Added early Share Play tests and upgraded metadata rendering.
