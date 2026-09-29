# Mobile Feature List — Android / iOS v1

The flat version of [mobile-implementation-plan.md](mobile-implementation-plan.md).

Effort: **Port** = works as-is · **Platform** = needs an Android/iOS implementation ·
**New** = new work or a real blocker · **Out** = not in v1.

---

## Playback

| Feature | Effort | Notes |
|---|---|---|
| Local file playback | Platform | New `IPlaybackEngine`: ExoPlayer / AVAudioEngine |
| Queue, shuffle, repeat | Port | `PlayQueue` |
| Background audio, lock screen controls | Platform | Foreground service, `MediaSession` / `MPNowPlayingInfoCenter` |
| Audio focus, interruptions, headphone unplug | Platform | Platform policy only |
| Effects chain, parametric EQ | Platform | DSP ports; needs an insert point in the mobile engine |
| Podcast speed control | Port | `VariableSpeedSampleProvider` |
| Gapless / prepared next track | New | Never existed on desktop either (`blockers.md` gap D) |

## Library and playlists

| Feature | Effort | Notes |
|---|---|---|
| Playlists: create, edit, reorder, delete | Port | `PlaylistStore`, JSON in the app sandbox |
| Mixed local and Spotify playlists | Port | `PlaylistItem` models both |
| Pinning, sort order, cover art chain | Port | |
| Per-playlist default visualizer | Port | `VisualizerRef` |
| Smart playlists | Port | `SmartPlaylistEvaluator` |
| M3U import and export | Port | |
| Search, sort, filter | Port | `LibraryDatabase` (SQLite) |
| Library scan | Platform | Android: `MediaStore` + `READ_MEDIA_AUDIO`. iOS: document-picker import, no scan |
| Unavailable-item handling | New | Desktop paths won't resolve — grey out, don't drop |

## URL sources

| Source | Effort | Notes |
|---|---|---|
| Direct audio (mp3, flac, m4a, ...) | Port | |
| Untitled | Port | HttpClient resolver |
| SoundCloud | Port | Native resolver + widget fallback |
| BandLab | Port | Native resolver |
| Suno (incl. CDN links, bare clip IDs) | Port | Native resolver + lyrics |
| Spotify | Port | WebView embed, same as desktop |
| Short-link expansion, bare YouTube IDs | Port | `NormalizeInput`, `ExpandRedirectAsync` |
| Move `OpenUrlService` to Core | Platform | Prerequisite for all of the above |
| YouTube and generic catch-all | New | yt-dlp can't ship on mobile — decision pending |

## Visualizers

| Feature | Effort | Notes |
|---|---|---|
| All 16 built-in visualizers | Port | `AvaloniaVizCanvas` runs on mobile Avalonia unchanged |
| Visualizer picker | Port | |
| PCM tap to `VisualizerFrame` | Platform | Platform tap into `FeedExternalSamples` |
| Scripted (Jint) visualizers | Platform | Should work under iOS AOT — verify on device early |
| Installed / HTML visualizers | Platform | Rides on the WebView work |
| Mobile frame budget (30 fps, pause when hidden) | New | Battery |
| Portrait / 9:16 layout pass | New | Spectrogram, Stereometer, Piano Roll |
| WASM visualizers | Out | Blocked on desktop too, worse on iOS |
| Scripted visualizer authoring | Out | Desktop-only |

## Embedded visualizers and worlds

| Feature | Effort | Notes |
|---|---|---|
| CSP injection, capability gating | Port | `CapsuleTrustRuntime` |
| Capsule signature verification, trust store | Port | BouncyCastle Ed25519 |
| `.spectral` album worlds | Port | `AlbumWorldRuntime`; cache needs a mobile size cap |
| Embedded HTML, markdown, video | Port | |
| Capsule story mode | Port | Inherits whatever desktop has |
| `IWebViewHost` implementation | Platform | Android `WebView` / iOS `WKWebView` via `NativeControlHost` |
| Virtual host mapping (no `file://`) | Platform | `WebViewAssetLoader` / `WKURLSchemeHandler` |
| `spectral.*` JS bridge | Platform | `WebMessageListener` / `WKScriptMessageHandler` |

## Shared Play

| Feature | Effort | Notes |
|---|---|---|
| Join a room | Port | `SharedPlayJoinRuntime`, `SharedPlayRoomSocket` |
| Roles, capabilities, co-DJ | Port | |
| Vote-skip, reactions, queue requests | Port | |
| Host a room | Platform | Works, but gate uploads to Wi-Fi by default |
| `spectralis://` deep links | Platform | Intent filter / `CFBundleURLTypes` |
| App Links / Universal Links for join URLs | New | Tapping a Discord link should open the app |
| Background reconnect and resync | New | Mobile suspends sockets; desktop never had to care |

## Car

| Feature | Effort | Notes |
|---|---|---|
| Voice / assistant transport | Platform | Falls out of the media session |
| Android Auto | Platform | `media3` `MediaLibraryService` + `automotive_app_desc.xml` |
| `ICarBrowseTree` in Core | New | One tree, two adapters. Root: Playlists, Podcasts, Library, Recent |
| CarPlay | New | Needs the `com.apple.developer.carplay-audio` entitlement from Apple |

## Podcasts

| Feature | Effort | Notes |
|---|---|---|
| Chapters (sidecar, ID3 CHAP, MP4 chpl) | Port | `Spectralis.Core/Podcasts/` |
| Resume from position | Port | |
| Sleep timer | Port | |
| Speed control | Port | |

## Not in v1

Desktop keeps: OBS overlay and designer, video export, Song Wars host console, Lyrics
Timing Studio, Dead Zone designer, tag and batch tag editing, Randomizer wheel, Streamer
Queue host console, Discord Rich Presence, loopback capture, Spotify EQ monitor,
MIDI/KAR SoundFont playback, WASM visualizers, scripted visualizer authoring, parametric
EQ curve editor.

## Totals

| Port | Platform | New | Out |
|---|---|---|---|
| 30 | 17 | 10 | 2 |

Most of v1 is a straight port because Core was written against seams. The New column is
where the thinking is: YouTube resolution, the car browse tree, CarPlay's entitlement,
frame budget, background sockets, and unavailable-item UX.
