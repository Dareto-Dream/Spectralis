# Mobile Implementation Plan — Spectralis.App.Android / Spectralis.App.Ios

Two new Avalonia heads next to the desktop one, sharing `Spectralis.Core`, the design
tokens, and the visualizers.

v1 scope: play music, show the visualizers, join a Shared Play room, work in the car.
Creator and streamer tools stay on desktop.

Flat feature list: [mobile-feature-list.md](mobile-feature-list.md).

---

## Projects

| Project | TFM | Role |
|---|---|---|
| `Spectralis.App.Android` | `net10.0-android` | Android head, MediaSession service, Android Auto |
| `Spectralis.App.Ios` | `net10.0-ios` | iOS head, AVAudioSession, CarPlay scene |
| `Spectralis.Ui` (new) | `net8.0;net10.0-android;net10.0-ios` | Shared Avalonia bits: `Design/Tokens.axaml`, `Design/Controls.axaml`, `AvaloniaVizCanvas`, `VisualizerHostControl` |

`global.json` pins SDK 10.0.201. No workloads are installed yet — run
`dotnet workload install android ios` and confirm the TFMs before scaffolding. Drop to
`net8.0-*` if only the older manifests are available. Avalonia 11.3.2's `net8.0-*`
assets resolve fine under a `net10.0-*` TFM.

`Spectralis.Ui` costs the desktop app one project reference and moves five files out
of `Spectralis.App`. The alternative is linked-file soup in two csproj files.

---

## Core split

`Spectralis.Core` is nearly all portable already — playlists, Shared Play, capsules,
lyrics, podcast chapters, scrobbling, the library database, every visualizer renderer.

One dependency blocks it. The `NAudio` metapackage ships no `netstandard2.0` asset
(`net472`, `net6.0`, `net6.0-windows7.0`, `netcoreapp3.1` only). A mobile TFM restores
the `net6.0` leg, compiles clean, then fails at runtime on winmm. `NAudio.Core` is
`netstandard2.0`, and it holds `NAudio.Dsp.FFT`, `ISampleProvider` and `WaveFormat` —
all `VisualizerSampleProvider` and the effects chain need.

Plan: multitarget Core as `net8.0;net10.0-android;net10.0-ios`. Reference `NAudio.Core`
everywhere, the `NAudio` metapackage only for `net8.0`, and `<Compile Remove>` the
desktop-only files from the mobile TFMs:

```
Audio/AudioEngine.cs             replaced by IPlaybackEngine
Audio/WaveOutAudioDevice.cs      winmm
Audio/PulseAudioAudioDevice.cs   subprocess
Audio/AudioToolboxAudioDevice.cs subprocess
Audio/FfmpegWaveStream.cs        subprocess
Audio/Midi/MidiPlaybackStream.cs deferred
Audio/Loopback/**                not a mobile concept
Integrations/YtDlpService.cs     no subprocesses on iOS
Integrations/FfmpegLocator.cs    same
Integrations/Obs/**              desktop-only
```

A separate `Spectralis.Core.Shared` assembly would be tidier but means touching every
`using` in the tree for no runtime gain.

### New seam: IPlaybackEngine

`AudioEngine` is NAudio's decode chain and does not port. Every current `IAudioDevice`
except the Windows one also drives a helper binary over stdin, and mobile can't spawn
processes. So mobile implements one level up:

```csharp
public interface IPlaybackEngine : IDisposable
{
    Task LoadAsync(TrackInfo track, CancellationToken ct);
    void Play(); void Pause(); void Stop();
    TimeSpan Position { get; set; }
    TimeSpan Duration { get; }
    float Volume { get; set; }
    double Rate { get; set; }
    VisualizerFrame GetFrame(bool includeSpectrogram = false, bool includeRawFft = false);
    event EventHandler? TrackEnded;
}
```

- Android: Media3 `ExoPlayer`, with an `AudioProcessor` tapping PCM into
  `VisualizerSampleProvider.FeedExternalSamples` (that method already exists for the
  loopback path).
- iOS: `AVAudioEngine` + `AVAudioPlayerNode`, with `installTap(onBus:)` on the main
  mixer feeding the same method.
- Desktop: thin adapter over the existing `AudioEngine`, no behaviour change.

The visualizer pipeline then needs no mobile-specific code.

---

## URL sources

`OpenUrlService` lives in `Spectralis.App`, not Core, and handles eight source kinds.
How each behaves on a phone:

| Source | Mechanism today | Mobile |
|---|---|---|
| Direct audio | URL to the engine | Ports as-is |
| Untitled | HttpClient + scrape + signed URL | Ports as-is |
| SoundCloud | Native resolver (HTTP/JSON) | Ports as-is |
| BandLab | Native resolver (HTTP/JSON) | Ports as-is |
| Suno | Native resolver (HTTP/JSON) | Ports as-is |
| Spotify | WebView embed | Ports onto the mobile WebView |
| SoundCloud/Suno fallbacks | WebView embed | Same |
| YouTube | yt-dlp subprocess | Blocked |
| Generic catch-all | yt-dlp subprocess | Blocked |

iOS forbids spawning processes. Android allows it, but shipping per-ABI yt-dlp and
ffmpeg binaries through Play is not worth the fight.

**Step 1, needed either way:** move the portable parts out of `Spectralis.App/Services`
into `Spectralis.Core/RemoteAudio/` — `NormalizeInput`, `DetectTarget`,
`ExpandRedirectAsync`, `RemoteAudioResolveResult`, and the three native resolvers.
They are pure `HttpClient` and belong in Core. Both mobile heads then get six of eight
sources for free.

**Step 2:** put the yt-dlp gap behind a seam.

```csharp
public interface IRemoteMediaResolver
{
    Task<RemoteAudioResolveResult> ResolveAsync(Uri uri, bool quickOnly, CancellationToken ct);
}
```

Desktop keeps the local yt-dlp path. Mobile has two options:

- **A, backend resolver.** New Rust route `POST /resolve/v1/url` runs yt-dlp
  server-side and returns the same result shape with a short-lived signed stream URL.
  Real background playback and car support; costs bandwidth and moves the ToS exposure
  onto the server.
- **B, WebView only.** YouTube opens the embed widget like Spotify does now. Free and
  no new server surface, but no background audio, no car mode, no queue integration.

Recommendation: ship B in v1, add A behind a settings flag later.

---

## Playlists and library

`Playlist`, `PlaylistItem`, `SmartPlaylist`, `PlaylistStore`, `M3uParser` and
`SmartPlaylistEvaluator` all port. `PlaylistStore` writes JSON under
`LocalApplicationData`, which maps to the app sandbox. Mixed local/Spotify playlists,
pinning, sort order, cover art fallback and per-playlist `DefaultVisualizer` come along
unchanged.

Where the music comes from differs per platform:

- Android: scan `MediaStore.Audio` (`READ_MEDIA_AUDIO` on API 33+) plus the app
  documents folder. `LibraryScanner` and `LibraryDatabase` keep working; only
  enumeration changes, behind a new `ILibraryFileSource` seam.
- iOS: no user-visible music filesystem. Import via `UIDocumentPicker` from Files or
  iCloud Drive. The iOS library is imported, not scanned.

Playlist items pointing at desktop paths won't resolve. Show them greyed with a "not on
this device" subtitle rather than dropping them, so playlists survive round-tripping.

---

## Visualizers

All 16 built-ins draw through `IVizCanvas`, and `AvaloniaVizCanvas` is plain Avalonia
`DrawingContext` that runs on mobile unchanged. Once it moves to `Spectralis.Ui`, both
heads get the full catalog by reference.

What needs work:

- **Frame budget.** Default to 30 fps on mobile, 15 when the screen dims or the app
  backgrounds, and stop the render loop when Now Playing isn't visible. The desktop
  600-frame benchmark needs a mobile sibling.
- **Portrait layouts.** Renderers assume a wide canvas. Check Spectrogram, Stereometer
  and Piano Roll at 9:16, with an aspect guard in the host control rather than
  per-renderer hacks.
- **Scripted (Jint) visualizers.** iOS is AOT-only. Jint 3.x is a tree-walking
  interpreter so it should work, but verify on a device early. If it fails, hide them
  from the picker on iOS.
- **Installed/HTML visualizers.** Ride on the WebView work below.
- **WASM visualizers.** Out of scope. Blocked on desktop already (`blockers.md` Blocker
  3) and worse on iOS, where Wasmtime's JIT is a non-starter.

---

## Embedded visualizers and worlds

`IWebViewHost` already specifies virtual host mapping, no `file://`, injected CSP, and
a size-capped `postMessage` bridge. `AlbumWorldRuntime`, `CapsuleReader`,
`CapsuleTrustRuntime`, `EmbeddedModuleReader` and `EmbeddedMarkdownRenderer` are pure
Core and port unchanged.

Two new implementations, both via Avalonia's `NativeControlHost`:

| Member | Android | iOS |
|---|---|---|
| `MapVirtualHost` | `WebViewAssetLoader` | `WKURLSchemeHandler` |
| `Navigate` / `NavigateToString` | `loadUrl` / `loadDataWithBaseURL` | `load` / `loadHTMLString` |
| `ExecuteScriptAsync` | `evaluateJavascript` | `evaluateJavaScript` |
| `MessageReceived` | `WebViewCompat.WebMessageListener` | `WKScriptMessageHandler` |
| `NavigationCompleted` / `Failed` | `WebViewClient` callbacks | `WKNavigationDelegate` |
| `BrowserProcessId` / `AudioMuted` | null / no-op | null / no-op |

Both serve from a folder over an https-looking origin, so the no-filesystem-identity
rule holds.

`AlbumWorldCacheStore` and `AlbumWorldSessionStore` land in the app sandbox with the
same 30-day eviction, plus a total-bytes cap with oldest-first eviction.

---

## Shared Play

`SharedPlayJoinRuntime`, `SharedPlayRoomSocket`, `SharedPlaySessionController`,
`SharedPlayCdnClient` and the models are `HttpClient` + `ClientWebSocket` + JSON, and
run on both platforms as-is. Roles, caps, co-DJ, vote-skip and reactions come along
unchanged.

Work needed:

- **Deep links.** `spectralis://` needs an Android intent filter and an iOS
  `CFBundleURLTypes` entry, both routed into the existing `ExternalOpenKind.SharedPlay`
  path. Add App Links / Universal Links for the https join URL.
- **Background socket.** Both platforms suspend network when backgrounded. The room
  socket must reconnect and resync on foreground. `SharedPlayRoomSocket` already has a
  transport seam, so this is a reconnect policy, not a rewrite.
- **Hosting on cellular.** `SharedPlayCacheStore` zips and uploads whole tracks. Default
  hosting to Wi-Fi only, with an override and an upfront size estimate.
- **Foreground service.** Android needs the playback foreground service for a joined
  room to keep playing with the screen off; iOS needs the `audio` background mode. Same
  plumbing as normal playback.

---

## CarPlay and Android Auto

Car surfaces are template UIs. No visualizers, no custom layout, no WebView. Both need a
browse tree and a media session, so build the tree once in Core:

```csharp
public sealed record CarBrowseNode(
    string Id, string Title, string? Subtitle,
    byte[]? Artwork, bool IsPlayable, bool IsBrowsable);

public interface ICarBrowseTree
{
    Task<IReadOnlyList<CarBrowseNode>> GetChildrenAsync(string? parentId, CancellationToken ct);
    Task<TrackInfo?> ResolveAsync(string nodeId, CancellationToken ct);
}
```

One implementation over playlists, library, podcasts and recently played, with two thin
adapters. Root tabs: Playlists, Podcasts, Library, Recent.

**Android Auto:** `androidx.media3` `MediaLibraryService` over that tree, the same
`MediaSession` the phone UI drives, plus `automotive_app_desc.xml` and the
`com.google.android.gms.car.application` manifest metadata. Driver-distraction rules cap
list depth and item counts, so the tree needs paging and short titles.

**CarPlay:** `CPTemplateApplicationSceneDelegate` with a `CPTabBarTemplate` of
`CPListTemplate`s, and `MPNowPlayingInfoCenter` + `MPRemoteCommandCenter` for transport.
This needs the `com.apple.developer.carplay-audio` entitlement, which is requested from
Apple and is not guaranteed. Treat it as an external blocker, not a task. Nothing else
on iOS depends on it.

Only content that resolves without UI can appear in the car: local files, cached remote
audio, podcasts. Under option B above, YouTube items are simply absent, which is correct.

---

## Phases

0. **Scaffolding.** Both heads, `Spectralis.Ui`, Core multitargeting, an empty Avalonia
   window on a device and in the simulator.
1. **Playback.** `IPlaybackEngine` with ExoPlayer and AVAudioEngine. Background audio,
   lock-screen controls via `IMediaSessionService`, audio focus and interruptions.
2. **Library, playlists, Now Playing.** `ILibraryFileSource` per platform, playlist CRUD,
   queue, and the visualizer catalog against the mobile frame budget.
3. **URL sources.** Move `OpenUrlService` to Core, wire the six native resolvers, add the
   WebView embed surface.
4. **Shared Play.** Join first, then host. Deep links, background reconnect, Wi-Fi-only
   hosting default.
5. **Capsules and worlds.** Both `IWebViewHost` implementations, capsule trust UI,
   `.spectral` worlds, embedded HTML/markdown/video, installed HTML visualizers.
6. **Car.** `ICarBrowseTree`, Android Auto adapter, CarPlay adapter if the entitlement
   lands. Test in DHU/CarPlay Simulator and then in an actual car; they disagree.
7. **Store readiness.** Icons, splash, permission strings, privacy manifests, signing,
   TestFlight and Play internal testing.

---

## Not in v1

Desktop keeps: OBS overlay and designer, video export, Song Wars host console, Lyrics
Timing Studio, Dead Zone designer, tag and batch tag editing, Randomizer wheel, Streamer
Queue host console, Discord Rich Presence, loopback capture, Spotify EQ monitor,
MIDI/KAR SoundFont playback, WASM visualizers, scripted visualizer authoring, and the
parametric EQ curve editor.

Podcasts are in scope. `Spectralis.Core/Podcasts/` is fully portable and a phone is
where podcasts get listened to.

---

## Open questions

- **YouTube:** backend resolver or WebView only? Background playback and car support for
  YouTube links hang off this.
- **CarPlay entitlement:** apply to Apple now, or ship iOS v1 without it?
- **iOS library:** is document-picker import enough for v1, or does iOS need a
  desktop-to-phone sync path to be worth shipping?
- **Android distribution:** Play Store or sideloaded APK? Decides whether option A is
  even necessary.
- **Minimum OS versions:** Android 8 vs 10 (MediaStore permissions,
  `WebMessageListener`), iOS 15 vs 16 (Avalonia's floor, CarPlay templates).
- **Shared Play hosting from a phone:** in v1, or join-only until the upload story
  improves?
- **Settings sync:** does `AppSettingsStore` sync across desktop and mobile, or is each
  install independent? Independent is simpler for v1.

---

## Acceptance criteria

- Both heads build from the solution and run on a real device.
- A local file plays with the screen off, with working lock-screen transport.
- All 16 built-in visualizers render on Now Playing within the mobile frame budget.
- Desktop playlists open on mobile, with unavailable items clearly marked.
- The six native URL sources resolve and play; the WebView-backed ones display.
- A `spectralis://` link joins a room, follows transport, and survives backgrounding.
- A `.spectral` album world loads, renders, and drives playback through the bridge.
- Android Auto browses playlists and plays a track from a head unit.
- CarPlay does the same, or is documented as entitlement-blocked.
