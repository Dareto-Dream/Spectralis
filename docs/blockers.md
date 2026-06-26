# Current blockers — Avalonia app, 2026-06-13

Every item below is a non-✅ entry in `docs/feature-gap.md` that cannot be
implemented right now either because an external dependency is missing, a
platform is unavailable, or a core decision needs to be made first. Items that
are just "not yet written" (no external gate) are listed last under
**Implementable gaps** — those can be started immediately.

---

## Blocker 1 — Spotify integration ✅ UNBLOCKED

**Decision: env-var client ID + browser PKCE OAuth + Web Playback SDK.**

Client ID is baked into the assembly at build time via
`$env:SPECTRALIS_SPOTIFY_CLIENT_ID` → `AssemblyMetadata["SpotifyClientId"]`.
At runtime `SpotifyClientIdProvider` reads the metadata first, then falls back
to the live env var.  No user-supplied client ID is needed for official builds.

Ported to `Spectralis.Core/Integrations/Spotify/`:
- `SpotifyClientIdProvider` — metadata + env var resolution
- `SpotifyAuthCallbackServer` — localhost:5127 PKCE redirect listener
- `SpotifyTokenStore` — JSON credential file in `%LocalAppData%\Spectralis`
- `SpotifyService` — full PKCE flow + Web API (play/pause/seek/next/prev/
  transfer/queue/snapshot); opens the system browser for auth, same as legacy
- `SpotifyLyricsService` — fetches LINE_SYNCED LRC from the relay proxy

**Remaining work (no external gate):**
- Wire `SpotifyService` into the UI (settings link/unlink button, NowPlaying
  source selector, SMTC handoff, Discord presence, scrobbling from Spotify
  playback snapshot).
- Web Playback SDK injection into `IWebViewHost` (same pattern as legacy
  `Form1.Spotify.cs` — injects the SDK script, listens for `player_state_changed`).
- `SpotifyLoopbackCapture` (process-loopback visualizer feed) — can slot into
  the existing `ILoopbackCaptureSource` seam on Windows ≥ 10.0.20348.

**Affects:**
- `feature-gap.md` lines 73, 155, 185–187, 459–470, 489, 542 (partial)

---

## Blocker 2 — Shared Play session runtime ✅ UNBLOCKED

**Decision: port from legacy; backend changes are available if needed.**

Ported to `Spectralis.Core/SharedPlay/`:
- `SharedPlayCacheStore` — packages tracks as `spectralis-rich.zip`;
  adapted from `AudioTrackInfo` → Core `TrackInfo`; uses `LyricsLoader`
  for sidecar lyrics.
- `SharedPlayCdnClient` — direct port (namespace changes only).
- `SharedPlaySessionController` — adapted for `TrackInfo`; `ApplySettings`
  takes explicit params instead of the WinForms `AppSettings` object;
  logging is inlined (no App-layer dependency).

The CDN is already running — no new server-side work needed.

**Remaining work (no external gate):**
- Wire `SharedPlaySessionController` into `NowPlayingViewModel`
  (`NotifyPlaybackChanged` on play/pause/seek/track-load, tick timer).
- Build the Shared Play sidebar view (host/join dialog, status panel,
  queue sync display, Live Channel toggle).
- Handle `ExternalOpenKind.SharedPlay` in `App.axaml.cs` (the stub log
  line is there; now the runtime exists to back it).

**Affects:**
- `feature-gap.md` lines 45, 184, 190, 493–510, 542 (partial)

---

## Blocker 3 — WASM / shader visualizer execution

**Gate: Wasmtime integration decision.**

The legacy app executes WASM instruction streams for embedded visualizers:
- Trust/capability context gates what WASM can call
- Picker locks to embedded WASM when a capsule declares one
- Installed/redeemable WASM packages need an execution context

The seam for WASM is implied by the module reader (`EmbeddedModuleReader` reads
the `DELTA_BIN_` frame and `EmbeddedVisualizer` on `TrackInfo`) but there is no
in-process executor. Shader execution has the same gap.

**What's needed to unblock:**
Recommendation: **Wasmtime.NET** (`Wasmtime` NuGet ≥ 30). The canvas API
(`IVizCanvas`) already exists; WASM modules call it through a host-function
table. The trust context (`CapsuleCapability`) already gates `visualizer.wasm`.

**Partial bypass available:**
Installed HTML visualizers already work. WASM packages show
"WASM execution not yet available" in the picker and fall back to the HTML
surface or built-in mode. No user-visible crash — just a missing feature.

**Affects:**
- `feature-gap.md` lines 111, 301, 324, 328–329, 553–558

---

## Blocker 4 — In-process update download and apply ✅ UNBLOCKED

**Decision: add Velopack; keep Squirrel compat until 5.1.0 (marked for removal).**

- `Velopack` (pinned exact, `Spectralis.App.csproj`) and `Squirrel.Windows`
  2.0.1 both referenced from `Spectralis.App.csproj`.
- `VelopackApp.Build().Run()` is the first call in `Program.Main` — handles
  apply/restart before Avalonia starts.
- `VelopackUpdateService` implements `IUpdateService`: checks via
  `UpdateManager`, falls back to `ReleaseFeedClient` when not running inside
  a Velopack install (dev/portable runs).
- `UpdateProgressWindow` shows a live `ProgressBar` during download; calls
  `ApplyUpdatesAndRestart` on success.
- Squirrel compat block in `Program.cs` handles `--squirrel-install/update/
  uninstall/firstrun` hooks from users upgrading from the WinForms release;
  each handler is labelled `// TODO 5.1.0: Remove`.
- `Spectralis.Installer/Windows/build-velopack.ps1` now runs `vpk pack`
  (CLI pinned via `.config/dotnet-tools.json`) and publishes to
  `releases-velopack/`, matching the CDN base `VelopackUpdateService` checks
  against. `build.ps1` runs the Velopack build and the Squirrel build
  (migration feed) together; pass `-SkipSquirrel` once all users have moved
  off the old feed.

**Resolved — no remaining work.** Both release channels are wired into
`build.ps1`; Squirrel removal is tracked by the `TODO 5.1.0` markers in
`Spectralis.App.csproj`, `Program.cs`, and `build.ps1`.

**Affects:**
- `feature-gap.md` lines 616–620

---

## Blocker 5 — macOS and Linux platform validation

**Decision: macOS = blind; validate on real hardware at release. Linux = WSL.**

Scripts compile and stubs return safe no-ops:
- `.dmg` build and code-signing hooks in `Spectralis.Installer/macOS/`
- `IMediaSessionService` macOS stub
- `ILoopbackCaptureSource` macOS stub (AVAudioEngine permission hint +
  BlackHole guidance)

**macOS:** no Mac hardware available in this environment. All macOS-specific
code will be validated on real hardware when compiling for the first public
release. The stubs are safe no-ops so Windows users are unaffected.

**Linux:** `wsl --install -d Ubuntu` is available. The `build-appimage.sh`
script can be run inside WSL for smoke tests. ELF binary output can be
inspected via `dotnet publish -r linux-x64`.

**Affects:**
- `docs/architecture.md` platform validation notes

---

## Implementable gaps (no external blockers)

These are missing but can be implemented right now. Ordered by estimated effort
and dependency depth.

### A — Reactive timeline: lyrics and shader dispatch

`OnReactiveParamsChanged` handles `"theme"` and `"visualizer"` targets.
The `"lyrics"` target (switch to lyrics panel, optionally highlight a line)
and `"shader"` target (stub: log + ignore until Blocker 3 resolved) are not
wired. This is a small extension to the existing switch statement in
`NowPlayingViewModel`.

### B — Album world cache store (30-day)

`AlbumCapsuleReader` opens `.spectral` packages but the runtime does not cache
the unpacked world to `%LocalAppData%\Spectralis\AlbumWorlds\{fingerprint}\`.
A 30-day eviction policy (check `manifest.json` mtime) and a
`AlbumWorldCacheStore` class are needed. The `ClearCachedAlbumState` menu
action already deletes this directory — the write side is missing.

### C — Album world session store

Per-track play stats (count, last-played timestamp), achievement unlock state,
and sequential level-gate progress need a `session.json` sidecar next to the
cached world. `AlbumWorldCacheStore.GetSession` / `SaveSession` methods would
cover it. The JS bridge (`saveBookmark` message) already exists in
`WebViewHostService` but has nowhere to persist the data.

### D — Gapless / prepared-engine handoff

When a remote URL is cached (YouTube, SoundCloud, Suno, BandLab), the audio
file is already on disk before the current track ends. `AudioEngine` has no
path to pre-load the next item. Adding a `PrepareNextAsync(path)` call that
opens a second `MediaFoundationReader` and swaps it in at the zero-crossing
boundary would provide gapless playback for cached remote sources.

### E — Embedded video rendering and sync

`TrackInfo.EmbeddedVideo` carries a byte array (typically MP4). The WebView
HTML surface cannot play it directly. Two options:

1. Synthesize an HTML page that base64-encodes the video into a `<video>` tag
   and hands it to the existing `IWebViewHost.NavigateToString`. Zero new
   infrastructure needed — same pattern as the embedded Markdown path.

2. Write the bytes to a temp file and use Avalonia's `NativeVideoView` or an
   `av:MediaElement` if available.

Option 1 (HTML synthesis) is the path of least resistance and matches how
`EmbeddedMarkdownRenderer` works.

### F — Capsule story / visual-novel mode

`.spectralis` capsules can carry a `story` block in the manifest: an image
entry, a summary, a backstory, and a `pages[]` array. The Capsules view already
shows title/subtitle/summary/capabilities. Missing: a story pager that cycles
through `pages[]` entries (each page is an HTML fragment or image reference
inside the package) using the `IWebViewHost` surface. This is the same WebView
surface already used for embedded HTML — the story just needs a synthesized
host HTML that loads pages on demand.

### G — OBS visual designer canvas

The custom JSON text editor is the current power-user path. A visual designer
would be a modeless `ObsDesignerWindow` with:
- A `Canvas` control scaled to the user's OBS resolution
- Draggable/resizable `Border` handles for each widget in `ObsLayout.Widgets`
- A property panel on the right for widget type, field-level toggles, and opacity
- Live preview feed via the existing OBS HTTP push (or a static mock state)

This has no external dependency — `ObsLayout` / `ObsPreset` / `BuiltInObsPresets`
are all in Core. The window would call `_setObsLayout` on confirm, same as the
preset picker.

### H — Clipboard URL metadata prefetch and redirect expansion

Clipboard monitoring currently extracts the URL, shows a toast, and caches on
Play. Two missing behaviors:
1. Expand short links (bit.ly, t.co, etc.) via a single HEAD request before
   showing the toast, so the resolved domain appears in the subtitle.
2. Start caching the remote audio in the background while the current track
   plays, so playback is instant when the user taps Play.

Both are self-contained additions to `ClipboardUrlMonitorService`.

### I — SoundCloud and Suno WebView fallback

When native CDN resolution fails (rate-limited, geo-blocked, API key expired),
the legacy app falls back to embedding the platform's widget in a WebView. The
`IWebViewHost` seam exists; a fallback path in `SoundCloudResolver` and
`SunoResolver` that calls `IWebViewHost.Navigate(originalUrl)` with a
widget-embed wrapper would cover the gap.

### J — yt-dlp stdout live-parsing (YouTube/SoundCloud status)

`NowPlayingViewModel.RemoteStatus` already exposes a status string. The yt-dlp
download currently runs via `SafeProcessRunner` with buffered output — no
line-by-line progress is dispatched. Switching the runner to stream stdout lines
and parsing `[download] N% of ...` lines into `RemoteStatus` updates would give
the live download progress that the legacy app shows.
