# The legacy WinForms app

The original Spectralis is a Windows-only WinForms/.NET 8 desktop app. It
remains in the repository in **maintenance mode**: it is the feature reference
while the remaining modules port to the Avalonia system
([architecture.md](architecture.md)), and it keeps working for users until the
Avalonia build reaches feature parity. New feature work happens in
`Spectralis.Core` / `Spectralis.App` only.

## Where it lives

The legacy app lives entirely under [`legacy/`](../legacy/):

| Path | Contents |
|---|---|
| `legacy/Spectralis.csproj` | The shared player library (solution entry `Spectralis.Legacy`; assembly name `Spectralis.Core`) |
| `legacy/Startup/Startup.csproj` | Packaged entry point and the current legacy app version |
| `legacy/Forms/`, `legacy/Controls/` | WinForms UI |
| `legacy/Audio/`, `legacy/Visualization/`, `legacy/Lyrics/`, `legacy/Capsule/`, `legacy/Reactive/`, `legacy/SharedPlay/`, `legacy/AlbumWorld/`, `legacy/Obs/`, `legacy/Library/`, `legacy/Playlists/`, `legacy/Effects/`, `legacy/Scrobbling/`, `legacy/TagEditor/`, `legacy/Analysis/`, `legacy/SongWars/`, `legacy/VideoExport/`, `legacy/Theming/`, `legacy/Settings/`, `legacy/Logging/` | Feature modules (the port sources for the Avalonia system) |
| `legacy/build.ps1`, `legacy/setup.ps1` | Legacy release build and local publish |

Shared assets stay at the repo root and are linked into both apps:
`Assets/` (icon, SoundFonts), `yt-dlp.exe`, `ffmpeg.exe`, `legal.html`,
`docs/legal/`, `spectralis.nuspec`, `build/Generate-Icon.ps1`, and the
`releases/` Squirrel feed.

The root `build.ps1` / `setup.ps1` build the **Avalonia** app; the legacy
equivalents are the ones inside `legacy/`.

## Building and running

Requirements: Windows, .NET SDK per `global.json`, Microsoft Edge WebView2
Runtime for embedded HTML/video content.

```powershell
# Run from source
dotnet run --project .\legacy\Startup\Startup.csproj
dotnet run --project .\legacy\Startup\Startup.csproj -- "C:\path\to\track.mp3"

# Quick self-contained local build (publish-normal/)
.\legacy\setup.ps1

# Squirrel release artifacts (Setup.exe, RELEASES, full packages in releases/)
.\legacy\build.ps1 -Version 1.2.1
```

Integration IDs are supplied the same way as the new app:
`SPECTRALIS_DISCORD_CLIENT_ID`, `SPECTRALIS_SPOTIFY_CLIENT_ID` (PKCE — public
client ID only, never a secret), `SPECTRALIS_SIGNTOOL_PARAMS` for signing.

## Upgrade path

The Avalonia app ships under the **same Squirrel app id** (`Spectralis`) and
the same `publish\Spectralis.exe` layout, so existing legacy installs upgrade
in place through the existing `releases/` feed
(`Spectralis.Installer/Windows/build-squirrel.ps1`; the first Avalonia release
is full-package-only via `-FirstAvaloniaRelease`).

## Data compatibility

- **Creator trust store** — shared. Both apps read/write
  `%LOCALAPPDATA%\Spectralis\trusted-creators.json` (trusted creators and the
  offline revocation cache) with the same format.
- **Library** — separate. Legacy: `%LOCALAPPDATA%\Spectralis\library.db`.
  Avalonia: `%APPDATA%\Spectralis\library-avalonia.db`. The new app offers a
  one-time import that re-scans every legacy path from disk (legacy tag
  overrides are intentionally discarded), logs missing files, and leaves
  `library.db` untouched — the legacy app keeps working after import.
- **Formats** — `.lrc`, `.lrc.json`, `.spectralis-reactive.json`,
  `.spectralis`, and `.spectral` files parse identically in both apps; the
  parsers were ported, not rewritten.

## Behavior differences in the Avalonia system

Intentional changes relative to this app; everything else is a straight port.

### Stricter format validation (security hardening)

- Reactive sidecars must explicitly declare
  `"format": "spectralis-track-reactive"` and `"formatVersion": 3`; an
  empty/partial JSON object no longer validates. Caps: 8 MB file, 2048
  sections, 16384 events, 256 params per event, finite non-negative times.
- Capsules gain size ceilings (768 MB file, 512 MB per zip entry, 8 MB
  manifest) and a bounded-copy guard that rejects entries whose decompressed
  size exceeds their declaration. Malformed public keys fail as invalid
  capsules instead of surfacing raw crypto exceptions.
- Lyrics sidecars larger than 4 MB are ignored.

### Reactive runtime: zero-duration events now fire

The WinForms `ReactiveRuntime.Seek` advanced `lastPosition` before computing
the elapsed-event window, so `set`/`reset` events with `duration: 0` never
fired during playback. The new runtime fires events in `(last, pos]` exactly
once and rebuilds state deterministically on seeks (including backwards).
Content that unknowingly relied on instant events being dropped will now see
them applied.

### Shuffle pins the playing track

Enabling shuffle (or mutating a shuffled queue) pins the current track to the
front of the fresh order so one pass visits every other item exactly once; the
WinForms queue left it mid-order, silently skipping everything shuffled before
it. Repeat-all wraps still draw a fresh unpinned order.

### Playback state machine

`Stopped → Playing` is a legal transition. Consumers read
`PlaybackStateMachine.State` instead of raw `WaveOutEvent` state.

### yt-dlp invocation

Only `http(s)` URLs resolve; `file:`, `javascript:`, raw option strings, and
non-URLs are rejected before any process starts. Resolution is bounded (60 s
timeout with process-tree kill, 1 MB captured output per stream).

### WebView engine

Capsule HTML and album worlds render in CefGlue/Chromium on all platforms
instead of WebView2. A `window.chrome.webview.postMessage` shim keeps content
written against the legacy bridge working unchanged; a strict CSP is injected
ahead of capsule markup (network closed unless the `webview.networkAccess`
capability is granted).

### Audio device selection

The interim device enumerator exposes only the system-default output (the
legacy app also always played to the default device). Full WASAPI enumeration
returns with the Settings audio page.
