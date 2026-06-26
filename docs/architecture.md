# Architecture — the Avalonia system

How the current Spectralis system is put together: project boundaries, the
seams that keep it testable and cross-platform, and what remains to port from
the legacy feature set.

## Projects

| Project | Target | Role |
|---|---|---|
| `Spectralis.Core` | `net8.0` (no Windows TFM) | Engine and domain logic — everything that doesn't touch a UI toolkit. Assembly name `Spectralis.Core.Next` (the legacy assembly already claims `Spectralis.Core`). |
| `Spectralis.App` | `net8.0`, Avalonia 11.3 + ReactiveUI | Views, viewmodels, design tokens, render adapters, platform glue |
| `Spectralis.Tests` | xUnit | Unit + integration tests and performance benchmarks (`Performance/`) |
| `Spectralis.Installer` | scripts | Squirrel (Windows), `.dmg` (macOS), AppImage (Linux) |

## Core modules

| Module | Contents |
|---|---|
| `Audio/` | `AudioEngine` (direct readers → MediaFoundation → fallback decode chain), `PlaybackStateMachine`, `PlayQueue`, MeltySynth MIDI rendering, loopback capture backends |
| `Formats/` | `.spectralis-reactive.json` models, hardened loader, `ReactiveRuntime` |
| `Metadata/` | TagLib reader (never throws on malformed files), SQLite `LibraryDatabase`, incremental `LibraryScanner`, `LegacyLibraryImporter` |
| `Lyrics/` | LRC parser (word timings, offsets), annotation parser, sidecar loader, `LyricsTimingSession` |
| `Capsule/` | Ed25519 `CapsuleReader` (verify on every load, zip-bomb guards), CDN key client, trust store with offline revocation cache, `CapsuleTrustRuntime` |
| `Visualizers/` | All eleven renderers against `IVizCanvas`, scene smoothing state, catalog |
| `SharedPlay/` | Protocol models, defaults/URL builders, `spectralis://` join-request validation |
| `Integrations/` | `SafeProcessRunner`, yt-dlp service, Discord Rich Presence, OBS overlay server, `WebViewHostService` (spectral.* bridge + CSP) |
| `Platform/` | The seams listed below |
| `Common/` | `TrackInfo`, format labels, time formatting, supported extensions |

## Seams

Every platform- or toolkit-specific dependency sits behind an interface in
`Spectralis.Core/Platform`; production implementations live in
`Spectralis.App/Platform` or `Core/Audio`.

| Seam | Purpose | Implementations |
|---|---|---|
| `IAudioDevice` / `IAudioDeviceEnumerator` | Audio output | `WaveOutAudioDevice` (legacy 70 ms/3-buffer path); silent fake in tests |
| `IVizCanvas` | Visualizer drawing surface | `AvaloniaVizCanvas` (DrawingContext); `NullVizCanvas` in tests; future raster targets (OBS, video export) |
| `IWebViewHost` | Embedded browser for capsule HTML / album worlds | `CefGlueWebViewHost` (WebViewControl-Avalonia, uniform Chromium on all platforms); fake in tests |
| `ILoopbackCaptureSource` | System-audio capture for the loopback visualizer | WASAPI (Windows), `pactl`/`parec` monitor source (Linux), setup-guidance stub (macOS, pending real-Mac validation) |
| `IMediaSessionService` | OS media controls | SMTC (Windows); MPRIS skeleton (Linux); macOS Now Playing pending |
| `IProtocolRegistrar` | Default app + `spectralis://` registration | `WindowsProtocolRegistrar` (HKCU); Linux handled by the AppImage desktop entry |
| `IEffectChainBuilder` | Effects rack hook | `EffectChain` with the Avalonia Effects Chain window |
| `SafeProcessRunner` | The only path to helper executables | Argument arrays, tree-kill timeouts, bounded output |

## Design system

All visual values resolve through `Spectralis.App/Design/Tokens.axaml`
(near-black surfaces, one signal-orange accent, three type roles including a
monospace data face, 4–64 px spacing scale, spline easings). No Fluent or
Material theme; `Design/Controls.axaml` restyles the Simple-theme control
templates from the tokens. Nothing is hardcoded in views.

## Security posture

- All untrusted formats are size- and structure-capped before deserialization;
  capsule zip entries use bounded copies (zip-bomb defense).
- Capsule signatures verify on every load; revoked keys reject from the local
  cache even offline; offline-with-no-cache rejects rather than allowing.
- Capsule HTML gets a strict CSP injected ahead of its markup; network stays
  closed unless the CDN-granted capability set includes
  `webview.networkAccess`. Bridge messages are size-capped and schema-checked.
- Helper executables (yt-dlp, ffmpeg) launch only through `SafeProcessRunner`
  with http(s)-only URL validation.
- `spectralis://` URIs are scheme-allowlisted with character-allowlisted
  session IDs and https-only CDN overrides.

## Testing

`dotnet test Spectralis.Tests` runs everything. Benchmarks (kept green in CI):
10k-file incremental rescan < 10 s, 50k track-row build + live search < 500 ms,
600 sustained frames of every visualizer < 33 ms each. Capsule tests sign real
fixtures with generated Ed25519 keys; the CDN, audio device, and WebView are
faked at their seams.

## Status & roadmap

Live in the Avalonia app: playback engine and queue, Now Playing with
visualizers and synced/annotated lyrics, library scan/search/watched folders,
playlists, tag editing, BPM/key analysis, scrobbling, Lyrics Timing Studio,
reactive timelines, capsule verification/trust with audio fallback, `.spectral`
album metadata recognition, OBS overlay (port 5128 by default), Discord Rich
Presence, mini player, Windows media session controls, default-app/protocol
registration, single-instance handoff, subprocess and URI hardening, and
installers.

Still porting from the legacy feature set:

- Shared Play session runtime + view (protocol foundation is in Core; port
  `SharedPlayCdnClient` / `SharedPlaySessionController` from the legacy tree)
- Full capsule story/visual-novel mode and `.spectral` album-world runtime on
  the `IWebViewHost` seam
- Spotify account/player migration and Spotify-specific queue/resume behavior
- macOS Now Playing, system tray, and remaining media-key/platform polish
- Karaoke, scripted/installed visualizers, content warnings, Song Wars, and
  video export
- Deeper OBS designer/editor controls, Shared Play settings, and full
  update-download/apply UI

Platform validation notes:

- Linux: self-contained cross-publish verified from Windows (ELF + Skia/
  HarfBuzz natives). The in-WSL AppImage smoke test needs a WSL distribution
  installed (`wsl --install -d Ubuntu`), then run
  `Spectralis.Installer/Linux/build-appimage.sh`.
- macOS: `.dmg` script and signing/notarization hooks are written but
  unvalidated until a Mac session; the macOS Now Playing and AVAudioEngine
  loopback bindings stay stubbed until then.
