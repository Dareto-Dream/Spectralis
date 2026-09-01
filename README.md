This is now [README.md](README.md) implementation 3 because for some reason anything with proper prose or tables sourced from AI, is now considered "completely AI" so full disclosure this time

### Disclosure
This document has been 100% written by hand by I, DeltaVortex, and i can confirm that all information is sourced from this thing we evolved to have called a brain

# Spectralis
Spectralis is a user-oriented cross-platform music runtime built with AvaloniaUI and ReactiveUI on .NET 8

Spectralis is built around the center ideation that every song is not a simple auditory experience but instead a fully immersive one, tailored via signed capsules, interactive album worlds, reactive timelines, synced annotated lyrics, and live visualizers. To aid in the social perspective of the audio, it can be shared through Shared Play rooms, OBS overlays, and a Discord Rich Presence.

I am hella lazy so the legacy windows-only winforms app remains in the repo as a feature reference during migration periods; see [docs/legacy-winforms.md](docs/legacy-winforms.md).

## Repository layout

| Path | What it is |
|---|---|
| [`Spectralis.Core/`](Spectralis.Core/) | The main internal engine in charge of the audio pipeline, format handling, library management, lyric reading, capsule trust, visualizer logic, integrations, and platform seams |
| [`Spectralis.App/`](Spectralis.App/) | The actual desktop app designed with Avalonia and ReactiveUI, it manages viewmodels, designing choices, and visual glue |
| [`Spectralis.Tests/`](Spectralis.Tests/) | buncha tests for the app, including benchmarks |
| [`Spectralis.Installer/`](Spectralis.Installer/) | Old packing methods like Squirell for windows |
| [`docs/`](docs/README.md) | Contracts, Architecture, and Guidelines |
| [`backend/`](backend/) | Rust backend for Shared play and Streamer queues (Railway) |
| [`web-share/`](web-share/), [`extension/`](extension/) | An extension format that is pretty outdated |
| [`discord-bot/`](discord-bot/) | A discord bot containing Queueing capability |
| [`legacy/`](legacy/) | The legacy WinForms app, maintenance mode ([docs/legacy-winforms.md](docs/legacy-winforms.md)) |
| [`Assets/`](Assets/), `yt-dlp.exe`, `ffmpeg.exe`, [`build/`](build/) | Runtime assets and build helpers for all platform versions of the app |

## Features
- Plays mp3, wav, flac, ogg, opus, m4a, aac, midi, kar, wma, webm, mp4, aiff, and more.
- Library with metadata, cover art, watched folders, auto-scanning, live updates, search, filters, and sortable columns.
- Queue with auto-advance, playlist export, shuffle, and repeat.
- Remembers window size, position, and maximized state.
- .lrc lyrics, embedded lyrics, and .lrc.json annotations. Includes a Lyric Timing Studio.
- 11 built-in visualizers at 60 FPS.
- .spectralis-reactive.json for synced sections, events, and parameter changes.
- Signed .spectralis capsules with artist verification, stories, custom HTML pages, and audio fallback.
- Signed .spectral album worlds with interactive HTML, playback hooks, stats, bookmarks, and tracklist fallback.
- Streamer Queue with submissions, priority tiers, pay-to-skip, and Discord commands.
- OBS overlay with presets, live state, artwork, and lyrics.
- Discord Rich Presence, Listen Together, favorite tracks, and listening stats.
- System audio capture via WASAPI, PulseAudio/PipeWire, and macOS backends.
- File associations, spectralis://, drag-and-drop, and legacy WinForms library import.

## Setup

### Requirements

- .NET SDK `10.0.201` or a roll forward from `global.json`
- Windows, Linux, or macOS (Mac tends to have some build issues that have planned fixes in the roadmap)
- Internet access for key validation when accessing `.spectralis` capsules

### Run source

```powershell
dotnet run --project .\Spectralis.App
```

To play a music file on launch:

```powershell
dotnet run --project .\Spectralis.App -- "C:\path\to\track.extension"
```

### Push a local build

The easiest way to get an executable without creating an installer package:

```powershell
.\setup.ps1
.\setup.ps1 -Configuration Release -RuntimeIdentifier win-x64 -OutputDirectory publish-normal
```

### Tests

For the full set including all benchmarks
```powershell
dotnet test .\Spectralis.Tests 
```
or for requesting specific tests
```powershell
dotnet test .\Spectralis.Tests --filter "FullyQualifiedName!~Performance"
```

### Discord Rich Presence

Dicord RPC requires a Discord application/client ID, get one [here](https://discord.com/developers/applications). Set it before running or building:

```powershell
$env:SPECTRALIS_DISCORD_CLIENT_ID="your Discord application ID"
dotnet run --project .\Spectralis.App
```

The activity always includes a Spectralis download button, if Shared Play is active and a session link is ready, a Listen Together button will appear alongside on the RPC.

### Build release packages

Releases require spotify and Discord client IDs put in, without them the integrations will remain dead for anyone who installs the build. Set both before running any of the release scripts (they will refuse to run):

```powershell
$env:SPECTRALIS_SPOTIFY_CLIENT_ID="your Spotify app client ID"
$env:SPECTRALIS_DISCORD_CLIENT_ID="your Discord application ID"
```

```powershell
# Windows will produce both Velopack (releases-velopack/) and Squirrel (releases/)
.\build.ps1 -Version 2.0.0 -FirstAvaloniaRelease

# Linux produces an AppImage (run on Linux or similar with appimagetool on PATH)
./Spectralis.Installer/Linux/build-appimage.sh 2.0.0
```

```bash
# macOS - run on a Mac (cannot be cross-built). build.sh is the peer of build.ps1;
# it loads client IDs from .env and writes to the same releases-velopack/ feed dir.
./build.sh --version 2.0.0          # Velopack feeds: osx-arm64 + osx-x64
./build.sh --version 2.0.0 --dmg    # ...plus the standalone .dmg bundle

# Signing/notarization is opt-in via env vars (unsigned builds work without them):
#   SPECTRALIS_MAC_SIGN_IDENTITY, SPECTRALIS_MAC_INSTALL_SIGN_IDENTITY, SPECTRALIS_NOTARY_PROFILE
```

### Shared Play backend

This repo contains a Rust backend in [`backend`](backend/).

### Starting the server
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