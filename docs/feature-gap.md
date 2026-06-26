# Feature gap - legacy WinForms app vs. Avalonia app

Audit date: 2026-06-11. Source of truth: `legacy/` WinForms modules compared
against `Spectralis.App` and `Spectralis.Core`.

This file tracks every legacy feature, workflow, setting, menu item, toolbar
control, dialog, and small affordance that is not yet present in the Avalonia
app. "Core ported" means the non-UI model/service exists in the new projects,
but the user-facing flow or wiring is still missing.

## App shell, menus, status, and shortcuts

- ✅ Partial: Avalonia shell branding now uses `Assets/icon.png` in the app
  chrome/navigation surfaces, has a branded navigation rail, and custom
  main-window chrome. Remaining gaps:
  full menu/status strip parity and utility-window chrome parity.
- ✅ Partial: Top menu bar. File, Library, Playback, Tools, and Help menus now
  exist with separators, accelerator text, a menu-strip version label, checked
  section items, and enabled states for transport entries. Remaining gaps are
  the menu items for unported features, listed per menu below.
- ✅ File menu ported: Open Audio, Add to Queue, Open URL, Export Video...
  (FFmpeg H.264/AAC MP4 from visualizer frames + audio, resolution/FPS/mode
  picker, progress bar, cancel), Set as Default App... (registers protocol +
  file associations then opens ms-settings:defaultapps), Settings, P2W Mode
  (checkbox toggle, yellow banner overlay), and Exit.
- ✅ Library menu ported: Now Playing/Library Browser/Playlists checked items,
  Open Playlist (M3U), Save Queue as Playlist, Analyze Library BPM + Key,
  Listening Stats, and Scrobbling Settings. Library Folders live in
  Settings → Library.
- ✅ Tools menu: Effects Chain, Metronome, Karaoke Mode, Song Wars (Tools → Live),
  Lyrics Timing Studio (Ctrl+Shift+L menu entry wired; feature is partial — see
  Lyrics section), Scripted Visualizers, and OBS Overlay settings entry are all ported.
- ✅ Playback menu ported: Play/Pause/Open Audio label switching, Stop,
  Mute/Unmute label switching, Previous Track, Next Track, and enabled states
  driven by the queue.
- ✅ Help menu ported: Check for Updates, Redeem Visualizer... (dialog with
  redeem-key input, CDN download, install status, installed list),
  Clear Redeemed Visualizers... (count confirmation, deletes store),
  Terms of Service, Privacy Policy, Clear Cached Album State,
  About Spectralis, and Visit deltavdevs.com.
- ✅ Partial: Status strip ported: status text with playing accent coloring,
  queue "N of M" suffix, "via {ServiceLabel}" source suffix when a remote track
  is active (YouTube/SoundCloud/Suno/BandLab via `NowPlayingViewModel.SourceLabel`),
  output label, shortcut hint text, and the clickable "Made by DeltaVDevs" brand
  link. Remaining: joined Shared Play coloring (awaits Shared Play runtime).
- ✅ Partial: Legacy global shortcuts are restored for comma/period visualizer
  previous/next, Space play/pause, Esc session reset, Left/Right seek 5 seconds,
  Shift+Left/Right seek 30 seconds, Ctrl+Left/Right previous/next, Up/Down
  volume, M mute, Ctrl+O open, Ctrl+Shift+O add to queue, Ctrl+B library,
  Ctrl+P playlists, Ctrl+L open URL, Ctrl+Shift+L timing studio, Ctrl+Q
  Now Playing/queue destination, and Ctrl+, settings. Ctrl+Q now toggles the
  visible queue panel (and routes to Now Playing first when elsewhere), and the
  menu bar carries accelerator text.
- ✅ Current-time label click toggles elapsed vs. remaining time.
- ✅ Window title mirrors the active track as "Artist - Track - Spectralis".
- ✅ Window placement persistence: size, position, maximized state, startup
  restore after the window opens, off-screen clamping, and the Remember Window
  Placement setting in Settings > Playback.
- ✅ Session preservation ported: external file/URL opens reuse the running
  window when the Preserve Session setting (Settings → Playback) is on; the
  second launch forwards and exits.
- ✅ Single-instance/external-open IPC ported over the legacy named pipe:
  second launches forward files, URLs, and spectralis:// open requests
  (including queue-next/queue-end intents) to the running instance, which
  activates its window and routes them.
- ✅ Partial: `spectralis://` handling. Registration plus `spectralis://open`
  startup/second-instance routing with intent parsing are wired. Remaining:
  Shared Play join actions (await the Shared Play runtime).
- ✅ Partial: Windows SMTC/Now Playing ported: OS-level play/pause/stop,
  previous/next, timeline seek, metadata, and artwork via a Windows-targeted
  build flavor. Suno, BandLab, YouTube, and SoundCloud all route audio through
  the native engine so SMTC metadata and state updates work automatically.
  Remaining: Spotify external-player handoff (blocked on Spotify migration).
- ✅ Post-update "Spectralis updated" dialog ported: `SettingsViewModel.ConsumedUpdateVersion`
  exposes the version consumed at startup; `App.axaml.cs` shows a `MessageWindow`
  after the window settles when a Squirrel/Velopack update notice is present.
- ✅ About dialog is ported and accessible from Help → About Spectralis: version,
  executable path, install folder, runtime/OS/architecture, Copy Version,
  and Visit DeltaVDevs.
- ✅ Legal document viewer is ported with Copy and Open Page; accessible from
  Help → Terms of Service and Help → Privacy Policy.
- ✅ DeltaVDevs website link ported: About dialog and status strip both have the
  brand link (accent-colored, underline, `PointerPressed` → `OnBrandLinkPressed`,
  tooltip "Visit deltavdevs.com", Help → Visit deltavdevs.com menu entry).
- ✅ P2W Mode ported: File menu checkbox toggle, yellow (#FFC800) banner overlay
  "PAID SKIP - PAY 2 WIN" positioned top-right of the main window content area.

## Main now-playing surface and toolbar affordances

- The Avalonia app uses a navigation-rail layout rather than the legacy combined
  main panel with column switching. The separated album-art panel, column
  switching (visualizer/lyrics/queue/browser), and queue-aware lyrics column
  behavior are not ported as-is; the Avalonia shell achieves equivalent access
  via the navigation rail, an overlay queue panel (Ctrl+Q), and the lyrics
  surface within Now Playing. Not a gap to close — deliberate redesign.
- ✅ Track info "Show more info" setting ported: Settings → Appearance checkbox
  toggles `NowPlayingViewModel.ShowMoreInfo`, persisted in `AppSettings`, which
  hides the artist/album row and format/badge row in the Now Playing view.
- ✅ Album-art handling. `VisualizerCatalog.GetPreferredMode` falls back from
  art-dependent modes (AlbumCover, SpinningDisk) to MirrorSpectrum when no artwork
  is present. The `ShowArtworkSurface` state (VIZ cycler "OFF" position) displays
  cover art in a 380×380 panel with a branded `icon.png` fallback at 0.42 opacity
  when no embedded art is available — equivalent to the legacy per-track icon
  extraction but using Spectralis branding instead of the Windows shell icon.
- ✅ Partial: Visualizer toolbar. Previous/next visualizer, picker (built-in +
  scripted + installed "Special:" HTML entries), one-button VIZ/PEAK/OFF surface
  cycler with dots, YouTube as a fourth cycled surface when available, toolbar
  auto-cycle toggle, Settings-hosted cycle duration, response/sensitivity, and
  playback quality controls are all ported. Current visualizer name label:
  subtle opacity-0.38 overlay at the bottom-left shows the active visualizer name.
  Remaining: installed WASM/shader execution contexts.
- ✅ Tooltips for compact controls are complete: previous/next visualizer now
  show the keyboard shortcut (comma/period), the picker ComboBox explains
  scripted entries, and the surface-mode cycler and AUTO toggle carry full
  descriptive wording including available surface states and Settings pointer.
- ✅ Output sample-rate status label ported: status strip shows live
  EffectiveSampleRate updated each position tick via OutputRateText.
- ✅ Volume UX ported: volume label switching to "MUTED", mute remembering
  pre-mute level, M shortcut, and Up/Down keyboard stepping are ported.
  YouTube/Suno/BandLab/SoundCloud audio routes through the native engine so
  the volume slider already controls their playback. The YouTube WebView is
  intentionally muted (audio comes from the yt-dlp engine path). Remaining:
  volume mirroring for WebView audio fallback paths (not yet implemented).
- ✅ Partial: Stop/reset session behavior. Stop now returns the Avalonia player
  to the first-open empty state, clearing queue, local/remote playback,
  YouTube mode, lyrics/reactive state, remote temp audio, capsule state
  (`NowPlayingViewModel.SessionReset` event clears `CapsulesViewModel`), and
  visualizer frame (`VisualizerHostControl` snaps to blank when `engine.IsLoaded`
  is false, calling `VisualizerSceneState.Clear()` on the next tick).
  Remaining parity: album worlds and Shared Play state.
- ✅ Drag/drop/file-open parity: Avalonia accepts files/folders on the shell,
  routes `.spectralis` and `.spectral` packages into Capsules, Queue by
  Default/Autoplay on Open apply to file drops, and single-instance
  forwarding, protocol URL handoff, and preserve-session behavior are wired.
- ✅ Visible queue toggle button and right-side queue panel are ported (QUEUE
  toolbar toggle plus Ctrl+Q).

## Playback and audio engine behavior

- ✅ Effects chain ported: 10-band EQ, compressor, reverb, and vocal blend DSP
  live in Core, the rack hot-swaps through `IEffectChainBuilder` with live
  engine rebuilds, and the Effects Chain window (Tools → Audio) provides the
  rack list, add/remove/reorder, per-effect enable, and parameter sliders.
- ✅ Audio settings ported: preferred output sample-rate (match source/44.1/48/
  88.2/96 kHz) and MIDI instrument picker are in Settings → Playback and
  persisted. Remaining: device selection controls (no equivalent in legacy).
- Gapless/prepared-engine handoff from clipboard/remote cached playback is
  missing.
- ✅ Remote audio cache lifecycle is complete in the UI: download, temp-file
  ownership, replacement cleanup, stop/reset cleanup, cache size display, and
  Clear Cache button in Settings → Diagnostics are all wired.
- ✅ Native external playback state sync for Suno, BandLab, YouTube, and
  SoundCloud: these all route audio through the native engine so OS-level
  metadata and state (SMTC, MPRIS) update automatically. Remaining: Spotify
  external-player sync (blocked on Spotify migration).
- ✅ Audio-device recovery error dialog ported: `DeviceRecoveryFailed` from the
  engine surfaces a themed message dialog asking the user to reconnect their
  audio device.
- ✅ Partial: Startup/open behavior distinguishes supported audio files,
  folders, `.spectralis` capsules, and `.spectral` album packages, with
  second-instance/protocol forwarding and preserve-session mode wired.
  Remaining: full album world playback.

## Queue

- ✅ Visible queue panel is ported: list, current item highlighting, playing
  indicator, two-line rows (title + folder), scroll-to-current, and keyboard
  Up/Down/Delete.
- ✅ Partial: Queue header details. "Queue - N tracks" and upcoming-count status
  are ported; "Spotify Queue" mode awaits the Spotify migration.
- ✅ Queue header controls are ported: Shuffle, Repeat: Off/All/One, Clear, and
  Add..., with active/disabled visual states.
- ✅ Queue context menu fully ported: Play, Play Next, Move Up, Move Down, Remove
  from Queue, Edit Tags, Content Warnings..., Add Files to Queue, Save Queue as
  Playlist, and Clear Queue are ported.
- ✅ Queue operations in UI: insert after current (Play Next), insert at end
  (Add), reorder, remove, delete key removal, double-click jump, clear, and
  save queue as playlist are ported.
- ✅ Queue mode supports remote URL items: `NowPlayingViewModel.QueueUrlAsync`
  adds a URL to the queue (displaying host as subtitle); `LoadQueueItemAsync`
  dispatches to `LoadUrlAsync` for http/https items and `LoadCurrentQueueTrackAsync`
  for local files; `spectralis://open?url=...` with `queue-next/queue-end`
  intent routes through `QueueUrlAsync`. Mixed local+remote queues auto-advance.
  Remaining: Shared Queue pointer (Shared Play runtime, blocked).
- Spotify queue display mode is missing.
- Local-interlude behavior is missing: queue local files while Spotify is active,
  pause/park Spotify, play locals, then resume/advance Spotify.
- ✅ Queue-by-default setting applies to file-open/drop paths, and external
  second-instance/protocol opens honor queue-next/queue-end intents.
- Shared Play queue notifications for add/remove/reorder/clear are missing.

## Library and metadata

- ✅ Library Browser parity. New Library has scan/search/sort, legacy filter
  modes (All Tracks, Artists, Albums, Genres), Add Folder, Rescan, Enter-to-play,
  double-click play, legacy columns Year, Plays, and Key. Context menu: Play,
  Play Next, Add to Queue, Edit Tags, Content Warnings..., Analyze BPM + Key.
  Footer shows group counts: "42 tracks · 6 artists" in Artists mode,
  "42 tracks · 8 albums" in Albums mode, "42 tracks · 4 genres" in Genres mode.
- ✅ Library settings are ported into Settings: watched folder list, Add Folder,
  Remove, Rescan Now, and Auto-scan on startup. The Avalonia Settings page saves
  immediately (no OK/Cancel) — an intentional modern UX choice, not a gap.
- ✅ File watcher ported: watched folders live-update on supported audio
  add/remove/rename via `LibraryWatcher`.
- ✅ Auto-scan on startup setting is ported and persisted.
- ✅ Library store ported: `library-avalonia.db`, scan/watch, play count
  tracking, BPM/key/year columns, and one-time legacy migration offer in the
  empty state are all wired.
- ✅ Play-count increment on local track load is ported.
- ✅ Library right-click context menu ported: Play, Edit Tags (single +
  multi-select batch), and Analyze BPM + Key.
- ✅ Tag editor ported: single-file editor with all legacy fields and
  cover art choose/remove, batch editor with per-field apply checkboxes,
  TagLib write-back with one-time `.bak` backup, MusicBrainz lookup with
  cover-art fetch, library refresh after save, and live reload of the currently
  playing track's displayed metadata via `RefreshCurrentTrackMetadataAsync`.
- ✅ BPM/key analysis ported: BpmAnalyzer (autocorrelation), KeyAnalyzer
  (Krumhansl-Schmuckler), AnalysisWorker, per-file analysis from the library
  context menu, and all-library analysis from the Library menu.
- ✅ Auto-analyze BPM on startup ported (`AutoAnalyzeBpm` setting, default on,
  toggle in Settings → Library), plus on-demand analysis when an unanalyzed
  track loads.
- ✅ Beat grid overlay ported: analyzed BPM renders tick marks under the
  scrubber, updating live when analysis finishes for the playing track.
- ✅ Metronome window ported: BPM spinner, audio click, beat flash, tap tempo,
  topmost behavior, and seeding from the current track's analyzed BPM.
- ✅ Listening stats dialog ported: This Week/This Month/All Time, scrobbles,
  hours, streak, top artists, top tracks.

## Playlists

- ✅ Static playlists ported: create with name prompt, edit (rename, add files,
  remove, reorder), save, delete, play, and JSON persistence in the legacy
  playlists folder so existing playlists carry over.
- ✅ Smart playlists ported: rules editor (field/op/value), match all/any,
  limit, sort field/direction, evaluation against the library, starred display,
  edit/delete/play.
- ✅ Playlist browser ported: Name, Tracks, Type columns, status count,
  double-click play, and context menu (Play, Edit, Export M3U, Delete).
- ✅ Playlist toolbar ported: New Playlist, New Smart, Import M3U.
- ✅ M3U import ported (with #EXTINF metadata).
- ✅ M3U open-and-load ported (Library → Open Playlist (M3U)).
- ✅ M3U export ported.
- ✅ Save Queue as Playlist ported in both the Library menu and the queue
  context menu.
- ✅ Playlist deletion confirmation ported.

## Lyrics, annotations, timing, and karaoke

- ✅ Lyrics Inspector ported: LyricsInspectorWindow shows all timed lines with
  mm:ss.cs timestamps, annotation dot indicators, current-line highlight with
  signal-color left border, annotation panel (title/time/body), auto-scroll to
  current line on open, click-to-select annotated rows. INSPECT button in
  NowPlayingView (accent when annotations present) opens or raises the window.
- ✅ Partial: Lyrics side panel. The Avalonia panel shows the full scrollable
  line list (active line highlighted in Signal color) rather than a prev/current/
  next 3-line view, providing more context. Word/segment highlighting and annotation
  display are ported: explanation text now shows inline below the lyric line in
  italicized muted text when present, plus an info icon with tooltip. Remaining:
  the legacy "previous/next only" framing is not replicated (intentional).
- ✅ Timing Studio fully ported: play/pause (P hotkey), live position label (80 ms tick), LINE / WORD
  mode toggle, per-line nudge buttons (±0.10 s / ±0.50 s), seek-to-line, Copy LRC, Export .lrc
  sidecar, Embed in Tags (writes LRC to USLT via `TagEditorService.WriteLyrics`). Word mode shows
  a chip canvas (`WrapPanel` per lyric line, accent-filled timed chips, accent-border selected chip,
  click-to-select), Tap (T) advances the chip cursor, Undo / Reset work in both modes.
- ✅ Lyric annotation authoring/editor flow ported: `LyricsAnnotationEditorWindow` opens from the
  Lyrics Inspector "Edit Annotations..." button (only when a local `.lrc.json` sidecar path is
  writable). Left pane lists all timed lines; selecting a line opens its annotation in a multi-line
  TextBox; `has-note` CSS class highlights annotated rows; Save writes via
  `LyricsExplanationParser.Save`. `CurrentTrackPath` exposed on `NowPlayingViewModel`; track path
  threaded through `LyricsInspectorWindow` constructor.
- ✅ Embedded structured lyric data read/write ported: `TagEditorModel.Lyrics`
  added, `TagEditorService` reads/writes `tag.Lyrics` (USLT/plain), and the
  Tag Editor window exposes a multi-line "Embedded Lyrics" field at row 9.
- ✅ Word-level karaoke rendering ported: LyricSegmentViewModel (past/active/future
  states), inline WrapPanel per-word display with seg/seg-past/seg-active CSS
  classes, segment position updated each tick from FindActiveSegmentIndex.
- ✅ Karaoke Mode ported: borderless topmost KaraokeWindow (Tools → Audio →
  Karaoke Mode…), syllable-level gold left-to-right fill wipe with line-level
  fallback, Space/Esc shortcuts, Vocal Remove slider wired to VocalBlendEffect,
  and live document handoff on track change via 50 ms position timer.
- ✅ SyllableBank ported to Spectralis.Core.Lyrics: pipe-override splitting,
  leading/trailing punctuation reattachment, 380+ English word dictionary.

## Visualizers

- ✅ Built-in visualizer modes ported: Album Cover, Piano Roll, Spectrogram,
  Stereometer, Loudness Meter now render in Avalonia.
- ✅ Legacy enum stubs (Led Meter, Vectorscope, Bounce Bars, Circular EQ,
  Block Grid) are carried in the new enum for settings compatibility; they have
  no renderers, same as legacy.
- ✅ Piano-roll MIDI visualizer ported, including falling notes, 88-key
  keyboard, active-key highlighting, and the MIDI instrument caption.
- ✅ Partial: Visualizer picker parity. Built-in options, previous/next controls,
  scripted visualizers ("Script: &lt;name&gt;" entries), and installed redeemable
  HTML visualizers ("Special: &lt;name&gt;" entries from `InstalledVisualizerStore`)
  now all appear in the picker. Selecting a "Special:" entry loads the stored
  HTML bytes via `InstalledVisualizerStore.LoadContent`, creates an
  `EmbeddedHtmlContext`, and switches to the embedded HTML surface (persists
  across track changes; reverts to track-embedded HTML when a capsule overrides).
  Remaining: installed WASM/shader runtime execution contexts.
- ✅ Visualizer auto-cycle guards complete: skips when embedded HTML surface,
  YouTube video, or video export is active (`IsSurfaceEmbedded`, `ShowYouTubeVideo`,
  `IsExporting` guards in `CycleVisualizerIfDue`); `VideoExportWindow` sets
  `NowPlayingViewModel.IsExporting` via callback on export start/finish.
- ✅ Peak hold and sensitivity user controls are ported through the compact
  surface selector and Settings page with persistence.
- ✅ Built-in visualizer frame pacing improved: non-spectrogram modes no longer
  clone the full spectrogram history on every render tick; the heavy history
  copy is requested only by the Spectrogram visualizer.
- ✅ Built-in visualizer palette now rereads runtime theme/accent tokens on
  every render frame (`ApplyPaletteFromTokens` in `VisualizerHostControl`),
  so accent changes — including embedded track themes applied via
  `AppThemeService.Apply` — immediately affect built-in visualizer colors.
- ✅ Scripted visualizers ported: Jint JS sandbox (JsVisualizerRuntime),
  ScriptCanvasContext (IVizCanvas-backed canvas API), ScriptedVisualizerStore,
  ScriptVisualizerRenderer, ScriptedVisualizerManagerWindow (list + editor +
  apply/save/import/export), wired to Tools → Creator → Scripted Visualizers…
- ✅ Partial: Redeemable visualizer infrastructure ported: RedeemableVisualizerClient
  (CDN manifest parsing, HTTPS download, data/binary asset resolution),
  InstalledVisualizerStore (LocalAppData persist/load/clear + `LoadContent` binary
  decoder), RedeemVisualizerWindow UI, Help menu Redeem/Clear commands, and
  "Special: &lt;name&gt;" picker entries that load the stored HTML into the embedded
  surface. Remaining: installed WASM/shader runtime execution contexts.
- ✅ Partial: Embedded per-track module detection is restored for legacy ID3
  `DELTA_MODULE_`/`DELTA_BIN_`/`DELTA_DATA_` frames and `.spectralis` /
  `.spectral` package visualizer descriptors. Remaining: WASM instruction
  stream execution, trust/capability context, and picker locking for embedded
  WASM modules.
- ✅ Partial: Embedded HTML visualizer host is present in the now-playing
  surface. Remaining: installed/redeemable HTML contexts and richer fallback
  telemetry.
- ✅ Visualizer-reactive ambient theme glow: legacy "ambient glow" colors are per-renderer paint details (glow pens/brushes) inside the visualizer canvas, not a window-chrome effect. No separate window glow feature exists in legacy to port — nothing to migrate.

## Embedded track experiences

- ✅ Partial: Embedded HTML rendering is now present in the user-facing
  now-playing surface. The host resolves legacy `delta-asset:`,
  `delta-bin:`, and `delta-data-json:` routes, injects the Spectralis bridge,
  and pushes audio frame data at 60 fps via `ExecuteScript` into a
  page-resident `requestAnimationFrame` pump (no IPC round-trip pull).
  Embedded pages also get a DPR cap for smoother canvas rendering. Installed
  (redeemable) HTML visualizers are loaded into the same surface from the
  picker ("Special:" entries). Remaining: full legacy fallback telemetry.
- ✅ Embedded Markdown rendering ported: `EmbeddedMarkdownRenderer` (Markdig pipeline, `ToHtmlPage`, `ToHtmlContext`). When a track has an embedded Markdown module but no HTML module, `ApplyEmbeddedModules` synthesizes an `EmbeddedHtmlContext` and hands it to the WebView HTML surface. Dark-themed CSS template included.
- Embedded video rendering and sync are missing.
- ✅ Partial: YouTube video mode. Open URL exposes YouTube audio playback,
  a fourth surface-cycle state for YouTube video, WebView visibility
  management, in-canvas exit, local embed host, and position sync are ported.
  Remaining: full legacy status telemetry (yt-dlp stdout live-parsing) and
  non-YouTube embedded video parity.
- ✅ Embedded content enable/disable setting ported: `EnableEmbeddedContent` AppSettings toggle; `ApplyEmbeddedModules` respects it; Settings UI checkbox under Appearance.
- ✅ Embedded track theme application ported: `EmbeddedThemeInfo` (Core), `DELTA_THEME` ID3 frame reader in `EmbeddedModuleReader`, `TrackInfo.EmbeddedTheme`; `ApplyEmbeddedModules` calls `AppThemeService.Apply(mode, accent)` when `UseEmbeddedTrackThemes` is on; `ClearEmbeddedModules` reverts to user settings.
- ✅ Partial: Embedded HTML audio-frame bridge is wired into the new
  now-playing surface. Remaining: album-world frame bridge and non-HTML
  embedded content bridges.
- ✅ WebView fallback ported: `IWebViewHost.NavigationFailed` event added; `CefGlueWebViewHost` hooks `WebView.LoadFailed` and raises it; `NowPlayingView.OnEmbeddedNavigationFailed` calls `StopEmbeddedHtmlMode()` and falls back to the artwork surface, with a warning log.

## Capsules and album worlds

- ✅ Partial: Internal capsule opener now opens signed `.spectralis` files from
  dialog/drop/startup routing without leaving Now Playing, verifies trust,
  extracts the declared audio entry into Now Playing with manifest metadata,
  detects package-level embedded HTML/WASM/Markdown/video descriptors, and
  hands embedded HTML to the now-playing surface. Remaining: story mode.
- ✅ Creator trust confirmation dialog ported: `CapsuleTrustWindow` shows the
  creator name, fingerprint, all requested capabilities with human-readable
  labels (elevated capabilities marked with an amber indicator), and content
  tags from the manifest story — replacing the old plain `ConfirmWindow` text
  prompt. `CapsuleTrustContext` bundles creator + capabilities + tags through
  the `CapsuleTrustRuntime` trust prompt callback.
- ✅ Capsule content warnings and capability disclosure ported: capability
  disclosure is embedded in `CapsuleTrustWindow`; `CapsuleCapability` constants
  are mapped to friendly descriptions with an elevated-risk tier for
  `visualizer.wasm`, `webview.networkAccess`, and `sharedPlay.packageUpload`.
- Capsule story/visual-novel presentation is missing.
- ✅ Partial: Capsule reactive timeline handoff ported. `ReactiveRuntime` is
  loaded via `.spectralis.reactive` sidecar (`ReactiveTimelineLoader.LoadSidecar`)
  on every local track load. `NowPlayingViewModel` advances the runtime each
  250 ms tick and on seek. The "REACTIVE" badge and current section label
  (`ReactiveSectionLabel`) are displayed in the Now Playing view. `ParamsChanged`
  events for `theme` target now call `AppThemeService.Apply` (gated by
  `UseEmbeddedTrackThemes`); `visualizer` target switches `SelectedVisualizer`
  to the named built-in mode. Remaining: `lyrics` and `shader` target dispatch,
  full visual-novel story mode, and WASM shader execution.
- ✅ Capsule drag/drop, normal file-open picker, and startup file-open routing
  are ported for `.spectralis` and `.spectral` package extensions.
- ✅ Partial: `.spectral` album packages are now signature-verified in Core and
  opened through the internal package loader with album metadata, track list,
  and per-track embedded visualizer descriptor detection. Remaining album world
  runtime: track asset playback handoff, intro/story, embedded browser/world
  view, JS bridge/message dispatch, synced frame updates, per-track stats,
  cache, and session persistence.
- ✅ Clear Cached Album State ported: Help menu item, confirm dialog, deletes
  %LocalAppData%\Spectralis\AlbumWorlds recursively with error reporting.
- Album world 30-day cache store is missing from the new UI/runtime.
- Album world session store is missing.
- ✅ Album world fallback control: `OpenSpectralAlbum` sets `HasPackage = false`
  and `Status = "Album world open failed: {message}"` on any loader exception,
  displaying the error in the Capsules view (parity with legacy error handling).
- ✅ Content warning system ported: `TrackContentWarningStore` (Core, `content_warnings.json`), `ContentWarningWindow` (pre-play tag-chip dialog, Play Anyway / Cancel), `ContentWarningEditorWindow` (comma-separated editor, Save/Clear/Cancel), queue context menu entry, and `ContentWarningPrompt` callback wired from MainWindow.

## Streaming, URLs, clipboard, and remote sources

- ✅ Open URL flow. File → Open URL... menu item (Ctrl+L) opens the themed
  dialog globally; empty Now Playing state also exposes Open URL; clipboard
  URL prefill, Enter/Escape, redirect expansion, and target detection are wired;
  `spectralis://open?url=…` protocol URLs and single-instance second-launch
  URL forwards both route through `LoadUrlAsync`. Source label ("via YouTube"
  etc.) shows in the status strip while a remote track is active.
- ✅ Partial: Supported URL targets. Direct audio links, Untitled public track
  links, Suno public/CDN links, SoundCloud tracks, and BandLab tracks now use
  their legacy native resolvers. YouTube and generic service URLs still use the
  legacy yt-dlp bridge where applicable. Spotify links are detected but still
  require the Spotify account/player migration.
- ✅ Partial: YouTube playback. URL/video-id detection, yt-dlp-backed cached
  audio playback, YouTube as the fourth state in the surface cycler,
  in-canvas exit control, local-page iframe host, muted sync, the
  strict-origin/referrer-policy embed fix, and download progress ("Caching
  YouTube audio... N%") via `IProgress<int>` in `RemoteAudioCache.DownloadAsync`
  are wired through Open URL.
  Remaining: full legacy status controls (yt-dlp output streaming is not live-parsed).
- ✅ Partial: SoundCloud playback. Short links, oEmbed widget-source
  extraction, SoundCloud client-id discovery, API resolve, progressive MP3
  stream resolution, artwork fetch, native cache playback, and download progress
  status ("Caching SoundCloud audio... N%") are ported.
  Remaining: WebView widget fallback and full legacy status controls.
- ✅ Partial: Suno playback. Suno URLs, CDN links, and raw clip IDs use the
  legacy native resolver, artwork fetch, MP3 cache path, and download progress
  status. `SunoClipInfo.LyricsText` is passed through `RemoteAudioResolveResult.LyricsText`
  and displayed as an unsynced description panel (`LrcParser.ParsePlainText`,
  `IsDescription = true`, no active-line highlighting).
  Remaining: WebView fallback when native CDN playback fails.
- ✅ Partial: BandLab playback. Track/revision/slug URL detection, shared-key
  handling, post API lookup, page-state JSON scan, static mixdown fallback,
  artwork metadata, native cache playback, and download progress status are ported.
  `BandLabResolvedTrack.LyricsText` passed through and displayed as unsynced
  description panel (same path as Suno). Remaining: full legacy status controls.
- ✅ Untitled playback ported: Remix context parsing, signed audio URL fetch,
  artwork fetch, native MP3 cache, and `untitled.log` are wired into Open URL.
- ✅ Direct remote audio link playback ported: download/cache, metadata read,
  temp-file cleanup, and remote title fallback are wired.
- ✅ yt-dlp user-facing wiring retained for Open URL playback of YouTube and
  generic service URLs when yt-dlp is available.
- ✅ Partial: Clipboard URL monitoring is ported with a Settings toggle,
  supported URL extraction outside the dialog, duplicate history, in-app Play
  copied URL toast with friendly per-service label (YouTube/SoundCloud/Suno/
  BandLab/Untitled/Spotify by host pattern in `BuildClipboardToastSubtitle`),
  Dismiss action, and 30-second auto-dismiss.
  Remaining: redirect expansion before prompt (short-link services),
  metadata/artwork prefetch, and prewarm/caching while current track plays.
- ✅ External API consent dialog ported: shown on first launch if not previously
  accepted; Proceed persists the accepted state, Exit closes the app.
- ✅ Remote audio cache controls ported: cache size shown in Settings →
  Diagnostics; Clear Cache button frees cached remote audio files.

## Spotify

- Spotify account linking UI is missing: client ID field, bundled/custom client
  ID behavior, browser OAuth PKCE callback, linked account status, link/unlink.
- Spotify token store is missing from the new app.
- Spotify Web Playback SDK device/WebView is missing.
- Spotify play/pause/previous/next/seek/queue/status integration is missing.
- Spotify URI/link handling is missing for tracks, albums, playlists, artists,
  and episodes.
- Spotify lyrics service is missing.
- Spotify loopback visualizer wiring is missing. Loopback seams exist in Core.
- Spotify local-file interlude and resume behavior is missing.
- Spotify scrobbling is missing.
- Spotify Discord/Shared Play state integration is incomplete.

## Scrobbling and listening history

- ✅ Last.fm scrobbling ported: settings, MD5-signed API client, now-playing
  updates, batch scrobble submission, and the browser token/session link flow.
- ✅ ListenBrainz scrobbling ported: token/username settings with validation,
  submit-listens client (single + import batches), and enable toggle.
- ✅ Offline scrobble queue ported (legacy `scrobble-queue.json`, retry with
  restore on failure, drain on startup and after linking).
- ✅ Scrobble history persistence ported (legacy `scrobble-history.json`,
  10,000-entry cap).
- ✅ Listening stats computation and dialog ported: This Week/This Month/All
  Time, scrobbles, hours, current/longest streak, top artists, top tracks.
- ✅ Scrobbling settings dialog ported (Library → Scrobbling Settings).
- ✅ Partial: Scrobbling integration covers local-file and remote-source
  playback (50%/4-minute threshold, 30-second minimum, 5-second tick).
  `NowPlayingViewModel.RemoteTrackLoaded` fires on successful URL loads
  (Suno, BandLab, YouTube, SoundCloud); `MainWindowViewModel` subscribes and
  calls `Scrobbling.NotifyTrackLoaded`. Remaining: Spotify (blocked).

## Shared Play and shared queue

- Shared Play view is placeholder-only. Missing host session runtime, join
  runtime, live polling/pulsing, receiver client, cache store, package store,
  and session controller behavior.
- Shared Play settings are missing: enable toggle, CDN base URL, Live Channel
  enable, channel id, owner token, display name.
- Host/join UI is missing: create room, copy/share link, listener counts, status
  messages, and stop/leave controls.
- Join-on-launch via `spectralis://` is missing.
- Synced playback is missing: play/pause/seek/track-load snapshots and drift
  correction.
- Synced lyrics and visualizer state are missing.
- Shared queue requests are missing: add/remove/reorder/clear, queue pointers,
  queue pull loop, and remote package pointers.
- Reactions/presence are missing.
- Live Channel links and stats are missing.
- Browser Shared Play handoff from Discord Listen Together is incomplete in the
  desktop app.
- Shared Play cache cleanup is missing.

## OBS and live/creator tools

- ✅ Partial: OBS server exists in Core/new app and can now be enabled/disabled
  from Settings while preserving the legacy-compatible browser source URL.
  Legacy configurator coverage so far: enable toggle, port editor, token/URL
  copy, preset picker (10 built-in presets), and custom layout JSON editor
  (raw JSON text area with "Validate and Apply"). Remaining: visual designer
  canvas with drag/resize widgets, preview, and per-widget field-level toggles.
- ✅ Partial: OBS settings in the new UI now include enable toggle, editable
  port (1024–65535), token display/copy/regenerate, browser URL copy, layout
  preset picker (10 built-in presets: Default, Stream Full, Song Wars Full, Karaoke,
  Compact, etc.) that writes `ObsLayoutJson` to `AppSettings` and bumps the layout
  version, and a custom layout JSON editor (paste raw `ObsLayout` JSON, click
  "Validate and Apply"; `ObsLayout.FromJson` validates and `_setObsLayout` applies
  it — surfaces error in `ObsStatus` label on parse failure). Remaining:
  drag/resize designer canvas and per-widget field-level toggles.
- ✅ OBS Song Wars bracket widget feed ported: `ObsOverlayCoordinator.GetActiveTournament` callback (set/cleared when `SongWarsWindow` opens/closes), `BuildObsSongWarsState()` populates `ObsOverlayState.SongWars` with submissions, matches, phase/round labels, focus slot, elimination count, next match, and winner every 250 ms push.
- ✅ Song Wars tournament mode ported: `SongWarsBracketEngine` (double-elimination bracket, seeding, grand finals), `SongWarsVoteTally` (threshold rules, Eliminated conversion, consecutive-match guard), `SongWarsSessionController` (phase transitions, vote submission, live tally, reveal), `SongWarsTournamentStore` (atomic JSON persistence, JSONL audit, path-traversal guard), `SongWarsWindow` (3-panel: Browser/Setup/Host, per-judge vote rows, match log, play A/B hooks). Tools → Live → Song Wars... menu entry wired.
- ✅ Scripted visualizer creator tooling ported: manager dialog, script editor,
  save/load/delete/import/export scripts, apply-to-visualizer live override.
- ✅ Video export ported: `VideoExportEngine` (FFmpeg H.264/AAC MP4 via PNG pipe, `VisualizerSampleProvider` FFT, `RenderTargetBitmap` off-screen render), `VideoExportWindow` (output path, resolution/FPS/visualizer dropdowns, progress bar, cancel), File → Export Video… menu item.

## Settings and persistence

- ✅ Partial: AppSettings JSON store. `settings-avalonia.json` persists playback
  and visualizer defaults (volume, visualizer choice, peak hold, response,
  auto-cycle, playback quality, MIDI instrument, autoplay, queue-by-default,
  preserve session, theme mode/accent, embedded track theme preference, window
  placement state, library folders, library auto-scan, OBS overlay
  enable/token/port, clipboard monitor toggle, and external API consent marker).
  Remaining: Spotify and Shared Play account/token sections (blocked).
- ✅ AppDataMigration ported: `AppDataMigration.MigrateLegacyFolder()` copies missing files from legacy `Spectrallis`/`AudioPlayer` data folders to `Spectralis` on startup.
- ✅ Partial: Full Settings dialog. New Settings exposes Appearance,
  Visualizer, Playback, Library, Integrations, OBS URL copy, Updates,
  Diagnostics, and default-app registration status; legacy integration
  accounts and advanced groups are still missing.
- ✅ Appearance settings ported: theme mode, accent picker/live preview,
  embedded track theme preference, embedded content toggle, and Show more info toggle.
- ✅ Theme modes ported: Dark, Light, OLED, Midnight.
- ✅ Accent choices ported: Amber, Ocean, Rose, Forest, Violet, Crimson, Cyan,
  Mint, Sunset, Gold.
- ✅ Partial: Visualizer settings. Built-in default visualizer, toolbar auto-cycle
  toggle, auto-cycle duration, peak hold, sensitivity, and EnableEmbeddedContent
  toggle are ported. Scripted visualizers appear as "Script: &lt;name&gt;" entries;
  installed (redeemable) HTML visualizers appear as "Special: &lt;name&gt;" entries
  and load into the embedded HTML surface. Remaining: installed WASM/shader
  runtime execution contexts.
- ✅ Playback settings ported: playback quality/output sample rate, MIDI
  instrument, default volume, autoplay on open, queue by default, remember
  window placement, and preserve session.
- ✅ Discord Rich Presence toggle ported and persisted in Settings.
- ✅ Partial: Integration settings. Remaining: Shared Play toggle, full updater
  download/apply behavior, Spotify client/link section, and deeper OBS designer
  controls. Automatic updates preference, Discord Rich Presence toggle, clipboard
  URL monitoring toggle, OBS enable/URL copy, default-app registration (Register
  Associations button in Settings → Integrations), and Windows protocol/file
  association handoff are all ported.
- ✅ Library settings ported in the main Settings surface: watched folders,
  Add Folder, Remove, Rescan Now, and auto-scan on startup.
- ✅ Audio settings ported: Effects Chain (Tools → Audio), preferred output
  sample rate, and MIDI instrument. Device/backend selection has no legacy
  equivalent to port.
- ✅ Partial: Updates settings. Auto-update preference, last-seen version,
  manual Check for Updates status, and ignored-version persistence (per-version
  "Don't remind again" via `IgnoredUpdateVersion` that auto-resets on next release)
  are ported. Remaining: Squirrel/Velopack download/apply/restart and
  `UpdateProgressDialog` (no package manager dep yet).
- ✅ Settings keyboard behavior ported: Escape from any non-NowPlaying section
  navigates back to Now Playing (only resets session when already on NowPlaying).
  Enter save is not applicable since Settings saves on each change.
- ✅ Themed custom scrollbar ported: `Controls.axaml` adds token-driven `ScrollBar`
  styles — 6 px thumb in `Brush.Ink.Muted`, signal accent on press, no line
  buttons, 0.7 → 1.0 opacity on hover, square corners. Applies globally.

## Theming, chrome, and custom controls

- ✅ Partial: Runtime theme system is now present in Avalonia. Theme mode and
  accent selection live-apply token brushes across the shell and existing
  Avalonia controls. Remaining gaps: menu/status surfaces that do not exist yet,
  and any future dialogs/surfaces added after this pass.
- ✅ Embedded track theme application ported: `DELTA_THEME` ID3 frame parsed into `EmbeddedThemeInfo`; `ApplyEmbeddedModules` calls `AppThemeService.Apply(mode, accent)` when `UseEmbeddedTrackThemes` is on; `ClearEmbeddedModules` reverts to user settings.
- ✅ Partial: WindowChromeStyler parity. The main Avalonia window now uses
  branded custom chrome with vector logo mark, window controls, and persisted
  placement. Remaining gap: chrome parity for utility windows/dialogs.
- ✅ Themed ToolStrip/MenuStrip/StatusStrip renderers: the new Avalonia shell
  uses native Avalonia Menu and a custom status area — no ToolStrip/MenuStrip/StatusStrip
  equivalents exist to port. Theme tokens apply to the Avalonia menu and status strip
  surfaces via shared resource dictionaries.
- ✅ Partial: Legacy custom controls are not ported one-for-one, but Avalonia
  Button, ToggleButton, ComboBox, CheckBox, Slider, ListBox, DataGrid,
  ScrollBar, and ToolTip now share token-driven styling. Remaining gaps:
  ModernButton/ModernComboBox/ModernSlider/ModernSwitch/ThemedScrollBar exact
  behavior and richer themed popups.
- ✅ Partial: Themed queue/list/browser custom painting. Library DataGrid and
  navigation list use token-driven styling; the visible queue panel and richer
  custom queue/list/browser painting are still missing.
- ✅ Partial: Dialog theming parity now covers current Avalonia utility dialogs
  through shared resources. Remaining gaps across settings, library, tag editor,
  playlists, scrobbling, OBS, update, legal, about, content warnings, trust,
  effects, karaoke, and video export.

## Packaging, updates, logging, and diagnostics

- ✅ Partial: In-app update check UI. Ported: auto-update preference, last-seen version,
  pending-update notice store, Settings status, update status dialog, `ReleaseFeedClient`
  (HTTP RELEASES parser), `UpdatePromptWindow` (Remind later / Update now / Don't remind again),
  ignored-version flow, browser-redirect for download, startup auto-check (8-second delayed
  background check when `EnableAutoUpdates` is on). Remaining gaps: Squirrel/Velopack
  download/apply/restart (no package manager dep yet), UpdateProgressDialog.
- ✅ AppDataMigration ported: copies missing files from legacy Spectrallis/AudioPlayer
  data folders to current Spectralis folder on startup.
- ✅ Manual "Check for Updates" action now runs the release-feed HTTP check, shows
  UpdatePromptWindow when an update is available, and opens the download page on confirm.
- ✅ Post-update "Spectralis updated" dialog ported: `SettingsViewModel.ConsumedUpdateVersion`
  exposes the version consumed at startup; `App.axaml.cs` shows a `MessageWindow`
  after the window settles when a Squirrel/Velopack update notice is present.
- ✅ Logging helpers are ported: `SpectralisLog`, `AppLogPaths`, startup logging,
  unhandled-exception logging, and diagnostics folder opening. Remaining gaps:
  subsystem-specific logs such as `untitled.log` and update-install logs.
- ✅ Diagnostics snapshot is ported: About shows version/runtime/OS/architecture,
  executable/install folders, settings path, logs path, and Settings can copy
  the snapshot to the clipboard.
- ✅ Partial: Packaging content. Legal markdown and `legal.html` are copied into
  app output. Remaining gaps: installer/update package wiring and package-time
  app identity/version metadata parity.
- ✅ Default-app flow ported: File → Set as Default App... registers protocol + file associations (`WindowsProtocolRegistrar`) then opens `ms-settings:defaultapps?registeredAppUser=Spectralis`. Settings panel also exposes Register Associations button.
- ✅ Partial: File association/open startup routing handles audio files, folders,
  `.spectralis` capsules, `.spectral` album packages, plain `http/https` URL args
  (fresh-launch and second-instance IPC forward), `spectralis://open` protocol args,
  queue-next/queue-end intents, and preserve-session forwarding.
  Remaining: Shared Play join actions (await Shared Play runtime).

## Smaller dialogs and utility flows still missing

- ✅ About, Terms of Service, Privacy Policy, update status, diagnostics copy, and
  logs-folder utility flows now have Avalonia entrypoints from Settings.
- ✅ Name input dialog ported (playlist creation/save-as), plus a themed
  confirmation dialog for destructive actions with customizable button labels.
- ✅ Themed single-button message dialog scaffold is present (`MessageWindow`);
  legacy notice flows are wired as their respective features land.
- ✅ Content warning dialog and editor ported: `ContentWarningWindow` (pre-play) and `ContentWarningEditorWindow` (queue right-click).
- ✅ Creator trust confirmation dialog ported for capsule opens.
- ✅ External API consent dialog ported (first-launch gate with Proceed/Exit).
- ✅ Library settings flow ported in Settings.
- ✅ Scrobbling settings dialog ported.
- ✅ Playlist editor and smart playlist editor dialogs ported.
- ✅ Tag editor and batch tag editor dialogs ported.
- ✅ Effects chain dialog ported as a modeless Avalonia window.
- ✅ Karaoke window ported (KaraokeWindow, borderless topmost, syllable fill-wipe).
- ✅ Metronome window ported.
- ✅ Lyrics inspector dialog ported: `LyricsInspectorWindow` with word-level segment display, P2W mode, accessible from Now Playing toolbar.
- ✅ Timing Studio canvas chip display ported: LINE / WORD mode toggle,
  WrapPanel chip canvas (timed/selected visual states), Tap advances chip
  cursor, Undo / Reset work in word mode, click-to-select chips.
- OBS visual designer canvas (standalone modeless window with drag/resize widget
  placement, live preview, per-widget field toggles) is missing. The custom JSON
  editor in Settings is the current power-user alternative.
- ✅ Redeem visualizer dialog ported: `RedeemVisualizerWindow` with key input, CDN download, and installed list. Help → Redeem Visualizer... and Clear Redeemed Visualizers... menu items wired.
- ✅ Stats dialog ported.
- ✅ Song Wars dialog ported: `SongWarsWindow` (Browser/Setup/Host panels, see above).
- ✅ Update prompt dialog ported: `UpdatePromptWindow` (Remind later / Update now / Don't remind again, ignored-version persistence, browser-redirect on confirm). UpdateProgressDialog still missing (needs Squirrel/Velopack dep).
- ✅ Video export dialog ported: `VideoExportWindow` with output path, resolution/FPS/visualizer dropdowns, FFmpeg progress bar, and cancel.
