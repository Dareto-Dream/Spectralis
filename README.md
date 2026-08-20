# Spectralis
Spectralis is an all inclusive cross-platform music runtime built with AvaloniaUI and ReactiveUI on .NET 8. 

Spectralis is built around the core philosophy that every song is not just some audio you can listen to but a full embedded experience via signed capsules, interactive album worlds, reactive timelines, synced annotated lyrics, and live visualizers. Streaming integrations aid in sharing this experience with others through Shared Play rooms, OBS overlays, and a Discord Rich Presence.

I am hella lazy so the legacy windows-only winforms app remains in the repo as a feature reference during migration periods; see [docs/legacy-winforms.md](docs/legacy-winforms.md).

## Repository layout (Thanks claude for the table)

| Path | What it is |
|---|---|
| [`Spectralis.Core/`](Spectralis.Core/) | Cross-platform engine: audio pipeline, formats, library, lyrics, capsule trust, visualizer logic, integrations, platform seams |
| [`Spectralis.App/`](Spectralis.App/) | Avalonia + ReactiveUI desktop app: views, viewmodels, design tokens, platform glue |
| [`Spectralis.Tests/`](Spectralis.Tests/) | xUnit suite — unit, integration, and performance benchmarks (300+ tests) |
| [`Spectralis.Installer/`](Spectralis.Installer/) | Packaging: Squirrel (Windows), `.dmg` (macOS), AppImage (Linux) |
| [`docs/`](docs/README.md) | Formats, contracts, architecture, guidelines |
| [`backend/`](backend/) | Rust/Axum Shared Play + Streamer Queue backend (Railway) |
| [`web-share/`](web-share/), [`extension/`](extension/) | Browser Shared Play player and Chromium extension |
| [`discord-bot/`](discord-bot/) | Discord bot: Streamer Queue slash commands, now-playing poller, reaction lifecycle |
| [`legacy/`](legacy/) | The legacy WinForms app, maintenance mode ([docs/legacy-winforms.md](docs/legacy-winforms.md)) |
| [`Assets/`](Assets/), `yt-dlp.exe`, `ffmpeg.exe`, [`build/`](build/) | Shared runtime assets and build helpers used by both apps |

## Features
- Plays a wide range of audio formats including `mp3`, `wav`, `flac`, `ogg`, `opus`, `m4a`, `aac`, `midi`, `kar`, `wma`, `webm`, `mp4 audio`, `aiff`, and more through readers, SF2, and system codecs.
- Reads track metadata such as the title, artist, album, and cover art; SQLite library with watched folders, auto-scanning, live edit watching, legacy filters, live search, and sortable columns.
- Queue songs with auto-advancing on the right side queue panel, export to playlists, control queue with shuffle and repeat toggles (off/all/one).
- The app remembers main window size, postition, maximized state, to be restored onto a visible montior
- Synced lyrics pulled from a `.lrc` file and embedded lrc text, include annotations via `.lrc.json` sidecars (Genius-style).
- Includes a Lyric Timing Studio to aid in creating and exporting `.lrc` sidecars
- Ships eleven built-in visualizers (Spectrum, Mirror Spectrum, Waveform, Spinning Disk, Radial Spectrum, Oscilloscope, VU Meter, Spectrum Wave, 3D Graph, Dancing Colors, 3D Sphere) rendered on a dynamic canvas at a crisp 60 fps
- Supports cinemtic metadata via `.spectralis-reactive.json` sidecars for section tracking, timeline events, and parameter transitions synced to playback
- Opens signed `.spectralis` capsules, which are Ed25519-verified artist packages that contain creator trust, package metadata display, and audio fallback playback. Spectralis 5.4.0+ supports stories: either a built-in click-through pager or a fully custom creator-authored HTML page with the same `window.spectral` playback hook album worlds get. See [docs/formats/spectralis-capsule.md](docs/formats/spectralis-capsule.md).
- Recognizes signed `.spectral` album packages, whic are interactive HTML "world" maps (Super Mario World-style level select, liner notes page, branching narrative — creator's choice) with a JS hook for playback control, track stats, and bookmarks, falling back to a plain tracklist when no world is defined. See [docs/formats/spectral-album-world.md](docs/formats/spectral-album-world.md).
- Streamer Queue, a standaone streamer request queue, featuring link/upload submissions, skip/super-skip priority tiers, stripe pay-to-skip, Discord bot integration for `/request`, `/skip`, `/queue` - seperate from Shared Play, with its own rooms and owner tokens
- live OBS overlay at `http://127.0.0.1:5128/obs/{token}` with layout presets, SSE state push, artwork cache-busting, and current/next lyric lines
- Integrates with Discord via a Discorp Rich Presence with a download button and a Listen Together button during Shared Play.
- DRP idle state showing favorite tracks and hours spent listening
- Loopback-capture visualizer seam with WASAPI on windows, PulseAudio/PipeWire on linux, and macOS backends.
- Registers as the default app for supported audio extensions and the `spectralis://` protocol; drag-and-drop of files, folders, and capsules.
- One-time import of the legacy WinForms library (fresh disk rescan, migration log).

## Setup

### Requirements

- .NET SDK `10.0.201` or a compatible patch roll-forward from `global.json`
- Windows, Linux, or macOS (Windows is the primary validated target today)
- Internet access for creator key verification when opening `.spectralis` capsules (falls back to the local cache)

### Run from source

```powershell
dotnet run --project .\Spectralis.App
```

To open a file immediately at launch:

```powershell
dotnet run --project .\Spectralis.App -- "C:\path\to\track.mp3"
```

### Publish a local app build

The easiest way to get an executable without creating an installer package (contained `win-x64` build in `publish-normal/`):

```powershell
.\setup.ps1
.\setup.ps1 -Configuration Release -RuntimeIdentifier win-x64 -OutputDirectory publish-normal
```

### Tests

For the full suite including performance benchmarks
```powershell
dotnet test .\Spectralis.Tests 
```
or for specific tests
```powershell
dotnet test .\Spectralis.Tests --filter "FullyQualifiedName!~Performance"
```

### Discord Rich Presence

Dicord RPC requires a Discord application/client ID, get one [here](https://discord.com/developers/applications). Set it before running or building:

```powershell
$env:SPECTRALIS_DISCORD_CLIENT_ID="your Discord application ID"
dotnet run --project .\Spectralis.App
```

The activity always includes a Spectralis download button, if Shared Play is active and a session link is ready, a Listen Together button opens the browser player.

### Build release packages

Releases require spotify and Discord client IDs baked in, without them the integrations will remain silently dead for anyone who installs the build. Set both before running any of the release scripts (they will refuse to run):

```powershell
$env:SPECTRALIS_SPOTIFY_CLIENT_ID="your Spotify app client ID"
$env:SPECTRALIS_DISCORD_CLIENT_ID="your Discord application ID"
```

```powershell
# Windows — produces both Velopack (releases-velopack/, the in-process update
# channel) and Squirrel (releases/, migration-only feed for existing
# WinForms/old-Avalonia installs; pass -FirstAvaloniaRelease exactly once for
# the first Avalonia release, or -SkipSquirrel once migration is complete).
# Root build.ps1 is a thin wrapper over the installer scripts.
.\build.ps1 -Version 2.0.0 -FirstAvaloniaRelease

# Linux — AppImage (run on Linux/WSL with appimagetool on PATH)
./Spectralis.Installer/Linux/build-appimage.sh 2.0.0

# macOS — universal .dmg with signing/notarization hooks (run on macOS)
./Spectralis.Installer/Mac/build-dmg.sh 2.0.0
```

For windows distribution, sign the artifacts as unsigned installers are far more likely to get flagged by reputation based endpoint tools:

```powershell
$env:SPECTRALIS_SIGNTOOL_PARAMS='/a /fd sha256 /tr http://timestamp.digicert.com /td sha256'
.\Spectralis.Installer\Windows\build-squirrel.ps1 -Version 2.0.0
```

### Shared Play backend

This repo has a Rust backend in [`backend`](backend/) or Shared Play and Listen Together. Production is hosted at `https://audioplayer-production-5b83.up.railway.app` and it serves the session/state/queue/presence/reaction/channel/package endpoints along with the browser player from the same origin:

```powershell
cargo run --manifest-path .\backend\Cargo.toml
```

Railway deployment is configured with
[`backend/railway.toml`](backend/railway.toml) and
[`backend/Dockerfile`](backend/Dockerfile); add a Railway volume at `/data`
for persistent package storage.

## Documentation

Full documentation lives in [`docs/`](docs/README.md).

| Doc | What it covers |
|---|---|
| [docs/architecture.md](docs/architecture.md) | The Avalonia system: projects, seams, modules, status & roadmap |
| [docs/legacy-winforms.md](docs/legacy-winforms.md) | The legacy WinForms app: building it, data compatibility, behavior differences |
| [docs/guidelines.md](docs/guidelines.md) | Product philosophy and feature direction |
| [docs/standards.md](docs/standards.md) | How to extend the app — visualizers, themes, settings |
| [docs/creator-tools.md](docs/creator-tools.md) | Creator workflows such as Lyrics Timing Studio and Lyric Explanations |
| [docs/formats/spectralis-capsule.md](docs/formats/spectralis-capsule.md) | `.spectralis` single-track capsule format |
| [docs/formats/spectral-album-world.md](docs/formats/spectral-album-world.md) | `.spectral` album world format |
| [docs/formats/reactive-timeline.md](docs/formats/reactive-timeline.md) | Reactive timeline sidecar format |
| [docs/formats/metadata-embedding.md](docs/formats/metadata-embedding.md) | ID3v2 embedded WASM/HTML/video modules |
| [docs/cdn-contract.md](docs/cdn-contract.md) | CDN endpoint shapes |
| [docs/api-contract.md](docs/api-contract.md) | Full service routing contract |
| [docs/legal/terms-of-service.md](docs/legal/terms-of-service.md) | Terms for official builds, hosted services, Shared Play, and integrations |
| [docs/legal/privacy-policy.md](docs/legal/privacy-policy.md) | Privacy disclosures |