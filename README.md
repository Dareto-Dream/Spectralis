# Spectralis

Spectralis is a cross-platform desktop music runtime built with AvaloniaUI and
ReactiveUI on .NET 8. It is not a basic audio player — tracks can carry rich
embedded experiences: signed capsules, interactive album worlds, reactive
timelines, synced annotated lyrics, and live visualizers, alongside streaming
integrations (Shared Play rooms, OBS overlays, Discord Rich Presence).

The original Windows-only WinForms app remains in the repository as the feature
reference during the migration; see
[docs/legacy-winforms.md](docs/legacy-winforms.md).

## Repository layout

| Path | What it is |
|---|---|
| [`Spectralis.Core/`](Spectralis.Core/) | Cross-platform engine: audio pipeline, formats, library, lyrics, capsule trust, visualizer logic, integrations, platform seams |
| [`Spectralis.App/`](Spectralis.App/) | Avalonia + ReactiveUI desktop app: views, viewmodels, design tokens, platform glue |
| [`Spectralis.Tests/`](Spectralis.Tests/) | xUnit suite — unit, integration, and performance benchmarks (245 tests) |
| [`Spectralis.Installer/`](Spectralis.Installer/) | Packaging: Squirrel (Windows), `.dmg` (macOS), AppImage (Linux) |
| [`docs/`](docs/README.md) | Formats, contracts, architecture, guidelines |
| [`backend/`](backend/) | Rust/Axum Shared Play backend (Railway) |
| [`web-share/`](web-share/), [`extension/`](extension/) | Browser Shared Play player and Chromium extension |
| [`legacy/`](legacy/) | The legacy WinForms app, maintenance mode ([docs/legacy-winforms.md](docs/legacy-winforms.md)) |
| [`Assets/`](Assets/), `yt-dlp.exe`, `ffmpeg.exe`, [`build/`](build/) | Shared runtime assets and build helpers used by both apps |

## Features

- Plays common audio formats including `mp3`, `wav`, `flac`, `ogg`, `opus`, `m4a`, `aac`, `midi`, `kar`, `wma`, `webm`, `mp4 audio`, `aiff`, and more through direct readers, SoundFont synthesis, and system codecs.
- Reads track metadata with title, artist, album, and cover art extraction; SQLite library with watched folders, startup auto-scan, live add/remove/rename watching, legacy filter modes, live search, and sortable columns.
- Play queue with shuffle, repeat (off/all/one), auto-advance, right-side queue panel, playlist save/export paths, and mini player window.
- Remembers the main window size, position, maximized state, and safely restores it onto a visible monitor.
- Supports synced lyrics from sidecar `.lrc` files and embedded LRC text, with contextual annotations via `.lrc.json` sidecars (Genius-style).
- Includes a Lyrics Timing Studio for tapping plain lyric lines to playback and exporting `.lrc` sidecars.
- Ships eleven built-in visualizers (Spectrum, Mirror Spectrum, Waveform, Spinning Disk, Radial Spectrum, Oscilloscope, VU Meter, Spectrum Wave, 3D Graph, Dancing Colors, 3D Sphere) rendered through a toolkit-agnostic canvas at a 60 fps budget.
- Supports track-reactive cinematic metadata via `.spectralis-reactive.json` sidecars — section tracking, timeline events, eased parameter transitions synchronized to playback.
- Opens signed `.spectralis` capsules — Ed25519-verified artist packages with creator trust, package metadata display, and audio fallback playback. See [docs/formats/spectralis-capsule.md](docs/formats/spectralis-capsule.md).
- Recognizes signed `.spectral` album packages and displays album/track metadata while the full album-world runtime continues migrating. The sandboxed Chromium `spectral.*` bridge remains the target seam for those worlds. See [docs/formats/spectral-album-world.md](docs/formats/spectral-album-world.md).
- Serves a live OBS overlay at `http://127.0.0.1:5128/obs/{token}` with layout presets, SSE state push, artwork cache-busting, and current/next lyric lines.
- Publishes Discord Rich Presence with a download button and a Listen Together button during Shared Play.
- Loopback-capture visualizer seam with WASAPI (Windows), PulseAudio/PipeWire (Linux), and macOS backends.
- Registers as the default app for supported audio extensions and the `spectralis://` protocol; drag-and-drop of files, folders, and capsules.
- One-time import of the legacy WinForms library (fresh disk rescan, migration log).

The detailed migration status — what is live in the Avalonia app versus still
porting from the legacy feature set (Shared Play sessions, Spotify, full album
worlds, tray behavior, and creator/export tooling) — is tracked in
[docs/feature-gap.md](docs/feature-gap.md).

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

The quickest way to get a runnable executable without creating an installer
package (self-contained `win-x64` build in `publish-normal/`):

```powershell
.\setup.ps1
.\setup.ps1 -Configuration Release -RuntimeIdentifier win-x64 -OutputDirectory publish-normal
```

### Tests

```powershell
dotnet test .\Spectralis.Tests                 # full suite incl. performance benchmarks
dotnet test .\Spectralis.Tests --filter "FullyQualifiedName!~Performance"
```

### Discord Rich Presence

Discord RPC needs a Discord application/client ID. Set it before running or building:

```powershell
$env:SPECTRALIS_DISCORD_CLIENT_ID="your Discord application ID"
dotnet run --project .\Spectralis.App
```

The activity always includes a Spectralis download button; when Shared Play is
active and a session link is ready, a Listen Together button opens the browser
player.

### Build release packages

Every release must bake in the Spotify and Discord client IDs — without them
those integrations are silently dead for anyone who installs the build. Set
both before running any release script (the scripts refuse to run otherwise):

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

For public Windows distribution, sign the artifacts — unsigned installers are
much more likely to be flagged by reputation-based endpoint tools:

```powershell
$env:SPECTRALIS_SIGNTOOL_PARAMS='/a /fd sha256 /tr http://timestamp.digicert.com /td sha256'
.\Spectralis.Installer\Windows\build-squirrel.ps1 -Version 2.0.0
```

### Shared Play backend

The repo includes a Rust backend in [`backend/`](backend/) for Shared Play and
Discord Listen Together. Production is hosted at
`https://audioplayer-production-5b83.up.railway.app`; it serves the session/
state/queue/presence/reaction/channel/package endpoints and the browser player
from the same origin:

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
