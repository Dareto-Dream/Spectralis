import { Package, Zap, MonitorPlay, Users, Layers, FileCode2, NotebookPen } from 'lucide-react'
import { DocTable, CodeBlock } from '../components/LearnDoc.jsx'

// Tutorial-depth write-ups adapted from docs/ for the public Learn section.
// Keep these in sync with docs/formats/*.md, docs/creator-tools.md, docs/standards.md
// (OBS Overlay section), and docs/api-contract.md (Shared Play section) when those change.

export const ARTICLES = [
  {
    slug: 'spectralis-capsule',
    icon: Package,
    label: 'Artist format',
    ext: '.spectralis',
    title: 'Inside a .spectralis capsule',
    summary: 'A 104-byte signed header glued to a ZIP archive — audio, art, lyrics, a reactive timeline, and WASM visualizers, all cryptographically verified.',
    body: (
      <>
        <p>
          A capsule is a 104-byte signed header glued to the front of a plain ZIP archive.
          The header is <code>magic (4) + format version (4) + Ed25519 public key (32) +
          Ed25519 signature (64)</code> — the signature covers every byte of the ZIP payload
          that follows, so a single flipped bit anywhere in the archive invalidates the whole file.
          The fingerprint used everywhere else in the trust flow is just <code>SHA256(public_key_bytes)</code>,
          lowercase hex.
        </p>

        <h3>ZIP contents</h3>
        <DocTable
          headers={['Path', 'Required', 'Description']}
          rows={[
            [<code key="m">manifest.json</code>, 'Required', 'The CapsuleManifest schema, described below.'],
            [<code key="a">audio/&lt;entry&gt;</code>, 'Required', 'The audio file; its path must match manifest.audio.entry.'],
            [<code key="r">reactive.json</code>, 'Optional', <>A reactive timeline document — see the <em>reactive timeline</em> lesson.</>],
            [<code key="i">assets/images/*</code>, 'Optional', 'Cover art; the first entry is used as album art.'],
            [<code key="d">assets/data/*.lrc</code>, 'Optional', 'LRC lyrics; the first .lrc entry found is loaded.'],
          ]}
        />

        <h3>Manifest schema</h3>
        <p>
          The manifest declares format version 3 (<code>spectralis-capsule</code>) and carries the
          track's identity, its own copy of the signature block, requested capabilities, and asset
          paths:
        </p>
        <CodeBlock>{`{
  "format": "spectralis-capsule",
  "formatVersion": 3,
  "id": "my-track-id",
  "title": "Track Title",
  "artist": "Artist Name",
  "release": { "year": 2026, "credits": [] },
  "signature": {
    "keyId": "creator-key-id",
    "fingerprint": "sha256-hex",
    "algorithm": "Ed25519",
    "value": "base64-sig"
  },
  "capabilities": ["webview.localContent"],
  "audio": {
    "entry": "audio/track.flac",
    "sha256": "lowercase-hex-sha256-of-audio-bytes",
    "durationSeconds": 210.5
  },
  "assets": {
    "images": ["assets/images/cover.png"],
    "data": ["assets/data/lyrics.lrc"]
  },
  "story": {},
  "suppressAppLyrics": false
}`}</CodeBlock>
        <p>
          Set <code>suppressAppLyrics: true</code> when the capsule's own visualizer already renders
          the lyrics — otherwise the app's side lyrics panel would compete with it for screen space.
          The LRC file can still ship in <code>assets/data</code> for the visualizer to read directly.
        </p>

        <h3>Story explainer</h3>
        <p>
          Capsules can opt into a click-through story explainer by adding a <code>story</code> object
          to the manifest. <code>CapsuleStoryRenderer</code> picks a presentation in this priority order:
        </p>
        <ol>
          <li><strong>Custom HTML</strong> — <code>story.entry</code>, if set and present in the ZIP.</li>
          <li><strong>Synthesized pager</strong> — built from <code>story.pages</code> or <code>story.chapters</code> if either is non-empty.</li>
          <li><strong>Backstory pager</strong> — a single synthesized page from <code>story.backstory</code>, if set.</li>
          <li>Nothing — no story surface is shown.</li>
        </ol>
        <p>
          For full creative control, ship your own HTML/CSS/JS story page — same idea as an album
          world's <code>world.entry</code>, just scaled down for a single track:
        </p>
        <CodeBlock>{`"story": {
  "entry": "story/index.html",
  "binaryAssets": { "bg": "story/assets/bg.webp" },
  "dataAssets": { "config": "story/assets/config.json" }
}`}</CodeBlock>
        <DocTable
          headers={['Field', 'Required', 'Description']}
          rows={[
            [<code key="e">entry</code>, 'Yes (custom mode)', 'Path within the ZIP to the HTML file that boots the story.'],
            [<code key="b">binaryAssets</code>, 'No', 'Named binary assets (images, fonts) available at the virtual host.'],
            [<code key="d2">dataAssets</code>, 'No', 'Named text/JSON assets available at the virtual host.'],
          ]}
        />
        <p>
          The custom story page gets the same <code>window.spectral</code> bridge that embedded HTML
          content gets everywhere else in Spectralis: <code>spectral.meta</code> for track metadata,
          <code>spectral.resume()</code> / <code>spectral.pause()</code> / <code>spectral.seek(sec)</code> to
          control playback, and <code>spectral.exit()</code> to leave the story and return to normal
          playback.
        </p>
        <p>
          If <code>entry</code> is absent, or the file it names isn't in the ZIP, the player falls back
          to the synthesized pager. Pages come from <code>story.pages</code> or <code>story.chapters</code>,
          each supporting a <code>title</code>, <code>speaker</code>, <code>text</code>, and an image
          override — only PNG entries are displayed, and the default explainer image is resolved from
          <code>story.image</code>, <code>story.imageEntry</code>, <code>story.explainerImage</code>,
          <code>story.characterImage</code>, or finally <code>assets/images/character.png</code>.
        </p>

        <h3>Opening flow</h3>
        <ol>
          <li><code>CapsuleReader.Read(path)</code> validates the SPCC magic bytes and version 3, verifies the Ed25519 signature, computes the fingerprint, and reads <code>manifest.json</code>.</li>
          <li><code>CapsuleCdnClient.FetchCreatorKeyAsync(fingerprint)</code> fetches the creator's key metadata from the CDN, falling back to the local trust cache on network failure.</li>
          <li>The capsule is rejected if the key is unknown, not <code>active</code>, or has a <code>revokedAtUtc</code> set.</li>
          <li><code>manifest.capabilities</code> is intersected with the CDN key's <code>allowedCapabilities</code> — any capability the capsule asks for that the key doesn't grant means rejection.</li>
          <li>If the fingerprint isn't already trusted, the <code>CreatorTrustDialog</code> is shown.</li>
          <li>Updated key metadata is cached, and the fingerprint is trusted on first approval.</li>
          <li>The audio is extracted to a temp file and its SHA-256 is checked against <code>manifest.audio.sha256</code>.</li>
          <li>The track loads into the playback engine using metadata straight from the manifest.</li>
          <li><code>reactive.json</code>, if present, loads via the same path a sidecar would use.</li>
        </ol>

        <h3>Rules</h3>
        <ul>
          <li>Capsule audio temp files are deleted when the capsule is unloaded or the app closes — no leftover junk in <code>%TEMP%</code>.</li>
          <li>Local audio and capsule audio never coexist in the engine — any new local file load unloads the current capsule first.</li>
          <li>Capsule files are not added to the play queue; they replace the current track instead.</li>
          <li>Trust decisions persist at <code>%LocalAppData%\Spectralis\trusted-creators.json</code>.</li>
        </ul>

        <h3>Capability constants</h3>
        <DocTable
          headers={['Capability', 'Common usage']}
          rows={[
            [<code key="1">webview.localContent</code>, 'Embedded HTML visualizer served from extracted capsule assets'],
            [<code key="2">visualizer.wasm</code>, 'Embedded WASM visualizer module'],
            [<code key="3">visualizer.multiLayer</code>, 'Composable multi-layer visualizer'],
            [<code key="4">visualizer.shaderPack</code>, 'Shader pack bundle'],
            [<code key="5">sharedPlay.hostCapsule</code>, 'Capsule can be hosted via Shared Play'],
            [<code key="6">sharedPlay.packageUpload</code>, 'Capsule assets may be uploaded for Shared Play'],
            [<code key="7">timeline.appControl</code>, 'Reactive timeline may issue app control events'],
          ]}
        />
      </>
    ),
    visual: {
      kind: 'code',
      filename: 'release.spectralis',
      code: `[4]  Magic .............. SPCC
[4]  Format version ..... 3
[32] Ed25519 public key
[64] Ed25519 signature
     ↓ ZIP payload
     manifest.json
     audio/track.flac
     reactive.json
     assets/images/cover.png
     assets/data/lyrics.lrc`,
    },
  },
  {
    slug: 'album-worlds',
    icon: Layers,
    label: 'Album format',
    ext: '.spectral',
    title: 'Album worlds: .spectral capsules',
    summary: 'A multi-track sibling to .spectralis that ships a whole album with an interactive HTML "world" the creator fully controls.',
    body: (
      <>
        <p>
          Where a <code>.spectralis</code> capsule ships one track with one optional experience, a
          <code>.spectral</code> capsule ships a whole album with an interactive HTML world the
          creator fully controls — a level-select map where each song is a level, interactive liner
          notes, a branching narrative, whatever the creator wants to build. Ship no world and the
          player falls back to a plain tracklist.
        </p>
        <p>
          The binary envelope is <strong>identical</strong> to <code>.spectralis</code> with one
          exception: different magic bytes (<code>SPAC</code> instead of <code>SPCC</code>). That
          means the same signing tool, CDN key infrastructure, and trust dialog used for single-track
          capsules work unchanged for albums.
        </p>

        <h3>Trust model</h3>
        <p>The trust check mirrors the single-track flow exactly:</p>
        <ol>
          <li><code>AlbumCapsuleReader.Read(path)</code> validates the SPAC magic bytes, version 1, and the Ed25519 signature.</li>
          <li><code>CapsuleCdnClient.FetchCreatorKeyAsync(fingerprint)</code> fetches key metadata, falling back to the local trust cache on network failure.</li>
          <li>Rejected if the key is unknown, not <code>active</code>, or revoked.</li>
          <li><code>manifest.capabilities</code> is intersected with the CDN key's <code>allowedCapabilities</code>.</li>
          <li>If not already trusted, the <code>CreatorTrustDialog</code> is shown.</li>
          <li>Metadata is cached and the fingerprint trusted on first approval.</li>
        </ol>
        <p>
          Trust decisions for both formats live in one shared store at
          <code>%LocalAppData%\Spectralis\trusted-creators.json</code> — but a creator trusted for a
          single-track capsule isn't automatically trusted for an album. Each format's capabilities
          intersect against the CDN key independently, and album capsules <strong>must</strong> declare
          <code>album.world</code> in <code>manifest.capabilities</code> (with the CDN key granting it)
          or the capsule is rejected outright.
        </p>

        <h3>Manifest schema</h3>
        <p>Format name <code>spectralis-album</code>, version <code>1</code>:</p>
        <DocTable
          headers={['Field', 'Required', 'Description']}
          rows={[
            [<code key="f">format</code>, 'Yes', <>Must be <code>"spectralis-album"</code>.</>],
            [<code key="fv">formatVersion</code>, 'Yes', <>Must be <code>1</code>.</>],
            [<code key="id">id</code>, 'Yes', 'Unique album ID — alphanumeric plus dash/underscore/dot, max 64 chars.'],
            [<code key="t">title</code>, 'Yes', 'Album display title.'],
            [<code key="ar">artist</code>, 'Yes', 'Artist name.'],
            [<code key="rl">release</code>, 'No', 'Year, credits, etc.'],
            [<code key="sig">signature</code>, 'Yes', 'keyId, fingerprint, algorithm, value — mirrors the binary header.'],
            [<code key="cap">capabilities</code>, 'Yes', 'Capability strings requested by the capsule.'],
            [<code key="st">story</code>, 'No', 'Optional intro explainer, reusing the single-track story schema.'],
            [<code key="w">world</code>, 'No', 'HTML entry point and assets for the interactive world.'],
            [<code key="tr">tracks</code>, 'Yes', 'Array of track entries.'],
          ]}
        />

        <h3>The world section</h3>
        <CodeBlock>{`"world": {
  "entry": "world/index.html",
  "binaryAssets": {},
  "dataAssets": {}
}`}</CodeBlock>
        <p>
          <code>world</code> is entirely optional. If it's absent, or <code>entry</code> doesn't
          resolve to a real file in the extracted album directory, the player shows a fallback
          tracklist UI instead of loading WebView2 — album title, artist, a scrollable track list with
          a checkmark on completed tracks, click to play.
        </p>
        <p>
          When a world is present, the extracted <code>world/</code> folder is mounted as a WebView2
          virtual host at <code>https://spectral-world.local</code>, configured with <code>DenyCors</code>.
          Relative paths in your HTML (<code>./assets/bg.webp</code>, <code>../data/config.json</code>)
          resolve normally — no CORS headaches, no base64 data-URI workarounds. Build it like a normal
          website. The world page has network access to that origin only; it cannot reach external URLs
          unless the creator explicitly requests a <code>webview.networkAccess</code> capability.
        </p>

        <h3>Track entries</h3>
        <DocTable
          headers={['Field', 'Required', 'Description']}
          rows={[
            [<code key="id2">id</code>, 'Yes', 'Unique track ID within this album, referenced by the JS API.'],
            [<code key="t2">title</code>, 'Yes', 'Track display title.'],
            [<code key="ar2">artist</code>, 'No', "Falls back to the album's artist."],
            [<code key="ae">audio.entry</code>, 'Yes', 'Path within the ZIP to the audio file.'],
            [<code key="as">audio.sha256</code>, 'No', 'SHA-256 of the audio bytes, for integrity checks.'],
            [<code key="ad">audio.durationSeconds</code>, 'No', 'Duration hint used by the JS API state.'],
            [<code key="ai">assets.images</code>, 'No', 'Cover art paths; first PNG used as album art.'],
            [<code key="adat">assets.data</code>, 'No', 'Data file paths; first .lrc loaded as synced lyrics.'],
            [<code key="vi">visualizers</code>, 'No', 'Per-track HTML/WASM visualizer descriptors — same schema as embedded modules.'],
            [<code key="tl">timeline</code>, 'No', 'Per-track reactive timeline events.'],
            [<code key="sup">suppressAppLyrics</code>, 'No', "true hides the app's lyrics panel when the visualizer renders its own."],
          ]}
        />

        <h3>Building the world page: the JS API</h3>
        <p>
          The world HTML page talks to Spectralis through <code>window.spectral</code>. Every callback
          is stubbed as a no-op before the page navigates, so the world never needs null checks.
        </p>
        <p><strong>Spectralis → world (callbacks Spectralis calls into your page):</strong></p>
        <DocTable
          headers={['Callback', 'Fires']}
          rows={[
            [<code key="c1">onReady(state)</code>, 'Once, after the page loads and the bootstrap script is injected. state is the full album + session state.'],
            [<code key="c2">onTrackChanged(info)</code>, 'When a track starts playing — id, title, artist, durationSeconds.'],
            [<code key="c3">onPlaybackFrame(frame)</code>, 'About every 33ms during playback — levels, peak, rms, time, active, trackId.'],
            [<code key="c4">onTrackCompleted(trackId, stats)</code>, 'When a track reaches its natural end — the world decides what happens next.'],
          ]}
        />
        <p><strong>World → Spectralis (messages your page posts):</strong></p>
        <DocTable
          headers={['Message', 'Does']}
          rows={[
            [<code key="m1">spectral.playTrack</code>, 'Start playing a track, optionally at a given positionSeconds.'],
            [<code key="m2">spectral.addToQueue</code>, 'Append a track to the queue, to continue after the current one.'],
            [<code key="m3">spectral.pause / spectral.resume</code>, 'Pause or resume the current track.'],
            [<code key="m4">spectral.seek</code>, 'Seek the current track to a positionSeconds.'],
            [<code key="m5">spectral.saveBookmark</code>, 'Save a named bookmark at a track + position, written to session.json immediately.'],
            [<code key="m6">spectral.exitWorld</code>, 'Return to the normal player UI, unloading the world.'],
          ]}
        />
        <p>Minimum setup for a world page:</p>
        <CodeBlock>{`window.spectral.onReady = function(state) {
  // state.tracks  — [{ id, title, artist, durationSeconds }]
  // state.session — { currentTrackId, currentPositionSeconds, trackStats, bookmarks }
  renderTrackList(state.tracks, state.session);
};

window.spectral.onTrackChanged = (info) => highlightTrack(info.id);
window.spectral.onPlaybackFrame = (frame) => updateProgress(frame.time, frame.active);
window.spectral.onTrackCompleted = (trackId) => markCompleted(trackId);

function playTrack(trackId) {
  window.chrome.webview.postMessage(
    JSON.stringify({ type: 'spectral.playTrack', trackId })
  );
}`}</CodeBlock>

        <h3>Session, stats, and caching</h3>
        <p>
          <code>state.session.trackStats</code> tracks <code>playedSeconds</code> and
          <code>completed</code> per track — enough to build a percentage-complete indicator or an
          unlock mechanic straight from <code>onReady</code>. Bookmarks saved via
          <code>spectral.saveBookmark</code> come back the same way, so the world can offer a "jump to
          key moments" list.
        </p>
        <p>
          Extracted albums are cached for 30 days from their last play, and cleaned up automatically —
          unless the listener pins the album from <code>File → Save Album</code>, which sets
          <code>pinned: true</code> and exempts it from cleanup indefinitely.
        </p>

        <h3>Notes for creators</h3>
        <ul>
          <li>Design a loading state — <code>onReady</code> can fire up to 200ms after the page finishes loading, so don't just show a blank white flash.</li>
          <li>Pause expensive animation loops when <code>frame.active === false</code> (player paused or stopped); use <code>onPlaybackFrame</code> to detect it.</li>
          <li>Provide an exit button that posts <code>spectral.exitWorld</code> so the listener can get back to the normal player.</li>
          <li>Absolute paths (<code>/assets/bg.webp</code>) work fine against the world origin, same as relative ones.</li>
        </ul>
      </>
    ),
    visual: {
      kind: 'code',
      filename: 'discography.spectral',
      code: `[4]  Magic .............. SPAC
[4]  Format version ..... 1
[32] Ed25519 public key
[64] Ed25519 signature
     ↓ ZIP payload
     manifest.json  (format: "spectralis-album")
     tracks/01-intro.flac
     tracks/02-title-card.flac
     world/index.html
     assets/images/cover.png`,
    },
  },
  {
    slug: 'reactive-timeline',
    icon: Zap,
    label: 'Sidecar format',
    ext: '.spectralis-reactive.json',
    title: 'The reactive timeline',
    summary: 'A JSON sidecar that drives theme, visualizer, lyrics, and shader changes synced frame-by-frame to playback position.',
    body: (
      <>
        <p>
          Any local file can carry a <code>.spectralis-reactive.json</code> sidecar — same base name,
          dropped right next to the audio, no re-encoding or tagging required. Inside a capsule, the
          same document lives at the ZIP root as <code>reactive.json</code> instead.
        </p>

        <h3>Document format</h3>
        <DocTable
          headers={['Field', 'Required', 'Description']}
          rows={[
            [<code key="f">format</code>, 'Yes', <>Must be <code>"spectralis-track-reactive"</code>.</>],
            [<code key="fv">formatVersion</code>, 'Yes', <>Must be <code>3</code>.</>],
            [<code key="s">sections</code>, 'No', 'Named time ranges with a mood label, used by the OBS overlay.'],
            [<code key="tl">timeline</code>, 'No', 'A list of events that fire at specific timestamps.'],
            [<code key="a">assets</code>, 'No', 'Referenced resources (reserved for future use).'],
            [<code key="sp">shaderPacks</code>, 'No', 'Referenced shader packs (reserved for future use).'],
          ]}
        />

        <h3>Sections</h3>
        <p>
          Named, non-overlapping time ranges — the first match wins when looking up the current
          section. The OBS overlay reads this to populate its section/mood display:
        </p>
        <CodeBlock>{`"sections": [
  { "start": 0.0,  "end": 32.0, "name": "intro", "mood": "ambient" },
  { "start": 32.0, "end": 96.0, "name": "verse", "mood": "building" }
]`}</CodeBlock>

        <h3>Timeline events</h3>
        <p>
          Each event fires at a timestamp and targets one subsystem. Numeric params interpolate
          smoothly over a <code>duration</code> using an easing curve; anything else just snaps at
          <code>t &gt;= 1.0</code>:
        </p>
        <DocTable
          headers={['Targets', 'Actions', 'Easing']}
          rows={[[
            <>
              <code>theme</code> <code>visualizer</code> <code>lyrics</code> <code>shader</code>
            </>,
            <>
              <code>set</code> <code>transition</code> <code>reset</code>
            </>,
            <>
              <code>linear</code> <code>incubic</code> <code>outcubic</code> <code>inoutcubic</code> <code>insine</code> <code>outsine</code>
            </>,
          ]]}
        />

        <h3>Runtime behavior</h3>
        <p>
          <code>ReactiveRuntime</code> fires <code>SectionChanged</code> and <code>ParamsChanged</code>
          events as the playback position advances. It's stateless between track loads —
          <code>Load(null)</code> resets it completely — and does a forward scan every tick, so seeking
          the timeline bar re-syncs instantly instead of replaying everything from zero. Events are
          always ordered by <code>time</code>.
        </p>

        <h3>Rules</h3>
        <ul>
          <li>The sidecar must sit next to the audio file with the same base name, no exceptions.</li>
          <li>Inside a capsule, the file must be named <code>reactive.json</code> at the ZIP root.</li>
          <li><code>format</code> and <code>formatVersion</code> are validated on load; mismatches are silently ignored.</li>
          <li>Invalid event targets or actions are skipped without error — fail quiet, not loud.</li>
        </ul>
      </>
    ),
    visual: {
      kind: 'code',
      filename: 'track.spectralis-reactive.json',
      code: `"timeline": [
  {
    "time": 32.0,
    "target": "visualizer",
    "action": "set",
    "params": { "mode": "PulseRing" }
  },
  {
    "time": 64.0,
    "target": "theme",
    "action": "transition",
    "duration": 2.0,
    "easing": "incubic",
    "params": { "accent": "Violet" }
  }
]`,
    },
  },
  {
    slug: 'embedded-experiences',
    icon: FileCode2,
    label: 'Embedded content',
    ext: 'ID3v2 TXXX',
    title: 'Embedded experiences in plain MP3s',
    summary: 'WASM visualizers, HTML overlays, Markdown liner notes, and synced video — packed straight into ID3v2 tags, no capsule required.',
    body: (
      <>
        <p>
          Embedded modules let creators ship rich animated experiences directly inside plain MP3
          files as portable, self-contained content — no separate download, no capsule, it just rides
          along in the file. They live in ID3v2 <code>TXXX</code> frames and load into isolated
          runtime contexts inside Spectralis.
        </p>

        <h3>Module types</h3>
        <DocTable
          headers={['type', 'runtime', 'What it does']}
          rows={[
            [<code key="v">visualizer</code>, <code key="v2">wasm</code>, 'WASM binary visualizer'],
            [<code key="h">html</code>, <code key="h2">html</code>, 'Rich HTML overlay or fullscreen experience'],
            [<code key="m">markdown</code>, <code key="m2">markdown</code>, 'Formatted text — liner notes, lyrics'],
            [<code key="vi">video</code>, <code key="vi2">h264 / vp9 / av1 / h265</code>, 'Embedded video synced to audio'],
          ]}
        />

        <h3>Module definition (DELTA_MODULE_ frame)</h3>
        <p>Stored in an ID3v2 <code>TXXX</code> frame with description prefix <code>DELTA_MODULE_</code>:</p>
        <CodeBlock>{`{
  "id": "my_visualizer",
  "type": "visualizer",
  "runtime": "wasm",
  "entry": "_start",
  "version": "1.0.0",
  "binaryRef": "my_visualizer_wasm",
  "dataRefs": { "config": "config_json", "theme": "theme_json" }
}`}</CodeBlock>
        <DocTable
          headers={['Field', 'Required', 'Description']}
          rows={[
            [<code key="id">id</code>, 'Yes', 'Unique identifier — alphanumeric, dash, underscore; max 64 chars.'],
            [<code key="t">type</code>, 'Yes', 'visualizer, html, markdown, or video.'],
            [<code key="r">runtime</code>, 'Yes', 'wasm, html, markdown, h264, vp9, av1, or h265.'],
            [<code key="e">entry</code>, 'Yes', 'WASM: export name. HTML/Markdown: omit. Video: codec string.'],
            [<code key="b">binaryRef</code>, 'Yes', "ID of a DELTA_BIN_ frame containing the module's binary."],
            [<code key="v">version</code>, 'No', 'Semantic version string.'],
            [<code key="d">dataRefs</code>, 'No', 'Object mapping binding names to DELTA_DATA_ block IDs.'],
            [<code key="wh">width, height</code>, 'No', 'HTML/Video pixel dimensions.'],
            [<code key="ap">autoplay</code>, 'No', 'Video: play on load (default false).'],
            [<code key="lp">loop</code>, 'No', 'Video: repeat (default true).'],
          ]}
        />

        <h3>Data blocks (DELTA_DATA_ frame)</h3>
        <p>Configuration, theme overrides, and text assets, stored in their own <code>TXXX</code> frames:</p>
        <CodeBlock>{`Description: DELTA_DATA_config_json
Text:        { "color": "#FF00AA", "thickness": 2.5 }`}</CodeBlock>
        <ul>
          <li>Frame encoding is UTF-8.</li>
          <li>Content is treated as JSON if it parses; otherwise raw text.</li>
          <li>Maximum size: <strong>64 KB</strong> per block.</li>
          <li>Accessed via <code>context.GetDataByBinding("bindingName")</code> in WASM, or <code>delta-data-json:bindingRef</code> in HTML.</li>
        </ul>

        <h3>Binary assets (DELTA_BIN_ frame)</h3>
        <CodeBlock>{`Description: DELTA_BIN_my_visualizer_wasm
Text:        <base64-encoded WASM binary>`}</CodeBlock>
        <ul>
          <li>Must be valid base64.</li>
          <li>Decoded size: ≤ <strong>256 KB</strong> (video: ≤ <strong>16 MB</strong>), enforced at load time.</li>
          <li>Supported types: WASM (<code>.wasm</code>), images (PNG, JPEG, WebP), video (MP4, WebM, MKV).</li>
          <li>Images referenced from HTML use <code>delta-asset:&lt;bindingName&gt;</code> or <code>delta-bin:&lt;binaryId&gt;</code> — the player inlines them as <code>data:</code> URIs inside the sandbox.</li>
        </ul>

        <h3>WASM visualizer</h3>
        <p><strong>Sandbox constraints</strong> — run in a zero-capability sandbox, no cheating:</p>
        <ul>
          <li>No filesystem, network, process, or OS API access.</li>
          <li>No host bindings beyond the three allowed imports below.</li>
          <li>Linear memory is isolated; no cross-module access.</li>
          <li>No eval or dynamic code generation.</li>
          <li><strong>Timeout:</strong> 500ms per frame; exceeded frames are dropped.</li>
          <li><strong>Deterministic:</strong> no real time, no real randomness, no external state.</li>
        </ul>
        <CodeBlock>{`env.audio_sample   — read a single audio sample (index → float32)
env.random_uint32  — pseudo-random number (seeded by frame, not real randomness)
env.time_ms        — elapsed milliseconds since track start`}</CodeBlock>
        <p>
          <strong>Drawing instructions</strong> — all coordinates normalize to <code>[0, 1]</code>
          relative to the canvas. <code>Line</code> takes <code>X1, Y1, X2, Y2, Color, Thickness (1–20px)</code>;
          <code>Rectangle</code> takes <code>X, Y, Width, Height, Color, Thickness, Filled</code>;
          <code>Circle</code> takes <code>CenterX, CenterY, Radius (0–1), Color, Thickness, Filled</code>.
          Colors are ARGB — hex, integer, or named .NET colors. Arc, Bezier, Polygon, Path, Text, Image,
          and gradient fills are planned for v1.1.
        </p>
        <p>Always clamp values and fall back gracefully on missing config — a visualizer that throws because a field is missing is a bad time for everyone:</p>
        <CodeBlock>{`var config = context.GetDataByBinding("config");
var strokeColor = TryReadColor(config?.TryGetString("color", "strokeColor"))
    ?? Color.FromArgb(255, 0, 255, 170);
var thickness = config is not null && config.TryGetNumber("thickness", out var t)
    ? Math.Clamp(t, 1f, 10f)
    : 2.2f;`}</CodeBlock>

        <h3>HTML module</h3>
        <p>
          Maximum <strong>512 KB</strong> of HTML + CSS + inline SVG, <strong>2 MB</strong> total with
          assets. Renders as a fullscreen overlay or alongside the visualizer, with DOM manipulation,
          canvas/SVG rendering, CSS animations, and <code>requestAnimationFrame</code> loops all
          available — sandboxed, with no access to the parent window.
        </p>
        <p>
          It <strong>cannot</strong>: reach <code>parent</code>, <code>top</code>, or
          <code>window.opener</code>; make network requests beyond <code>data:</code> URIs; load
          external scripts or stylesheets; use <code>eval</code>; or share <code>localStorage</code>
          with the app.
        </p>
        <p>
          Forbidden elements: <code>script</code> (managed separately), <code>iframe</code>,
          <code>object</code>, <code>embed</code>, <code>applet</code>, any <code>on*</code> event
          attribute, <code>form</code>, <code>input[type=file]</code>, <code>input[type=submit]</code>.
          Forbidden CSS: <code>position: fixed</code>/<code>absolute</code>, <code>z-index &gt; 1000</code>,
          <code>@import</code>, and non-data <code>url()</code> — inline styles get stripped, so use a
          <code>&lt;style&gt;</code> block instead.
        </p>

        <h3>Markdown module</h3>
        <p>
          Up to <strong>256 KB</strong> of CommonMark, converted to HTML and rendered in a sandboxed
          iframe. Headings, paragraphs, bold/italic, lists, code blocks, blockquotes, tables, HTTP(S)
          links, and embedded-asset images are supported. Raw HTML blocks, footnotes, and
          <code>javascript://</code>/<code>data://</code> links are stripped.
        </p>

        <h3>Video module</h3>
        <p>
          Up to <strong>16 MB</strong>, H.264/VP9/AV1/H.265 in MP4, WebM, or MKV. Audio is always the
          sync source — video follows it, looping if shorter. A seek or loop in the audio triggers the
          same in the video, and frames only update on audio frame boundaries.
        </p>

        <h3>Load-time validation</h3>
        <p>
          Every module: the module JSON must parse, <code>type</code> must be a known value,
          <code>binaryRef</code> must resolve, and every data block must be within its size limit.
          WASM additionally needs a callable <code>entry</code> export and a compiled size
          ≤ 512 KB. HTML needs valid UTF-8 (forbidden elements are stripped at parse time). Video needs
          a detected container whose codec matches the declared <code>runtime</code>.
        </p>
        <p>
          If any check fails, the module is silently skipped and the track just plays with defaults —
          a broken visualizer disappearing quietly beats crashing someone's playback.
        </p>

        <h3>Failure modes</h3>
        <DocTable
          headers={['Failure', 'Behavior']}
          rows={[
            ['WASM trap (divide by zero, OOB memory)', 'Frame marked invalid; previous frame re-displayed.'],
            ['WASM timeout (>500ms)', 'Frame abandoned; module flagged for future skipping.'],
            ['HTML script injection attempt', 'Offending element removed; rendering continues.'],
            ['Video corrupted frame', 'Frame dropped; last valid frame displayed.'],
            ['Video decoder lag', 'Playback catches up; no audio skipping.'],
            ['Any rendering error', 'Instruction skipped; rendering continues.'],
          ]}
        />
      </>
    ),
    visual: {
      kind: 'code',
      filename: 'ID3v2 frames',
      code: `TXXX:DELTA_MODULE_my_visualizer
  { "type": "visualizer", "runtime": "wasm",
    "entry": "_start", "binaryRef": "viz_wasm" }

TXXX:DELTA_BIN_viz_wasm
  <base64-encoded WASM binary>

TXXX:DELTA_DATA_config_json
  { "color": "#a882f2", "thickness": 2.5 }`,
    },
  },
  {
    slug: 'creator-tools',
    icon: NotebookPen,
    label: 'Creator workflow',
    ext: 'Ctrl+Shift+L',
    title: 'Built-in creator tools',
    summary: 'A Lyrics Timing Studio for tapping out .lrc files by ear, plus content warnings that gate playback behind a confirmation.',
    body: (
      <>
        <h3>Lyrics Timing Studio</h3>
        <p>
          Open <code>File → Lyrics Timing Studio…</code>, or press <code>Ctrl+Shift+L</code>. Built for
          timing lyrics by ear without alt-tabbing to a separate app, it can:
        </p>
        <ul>
          <li>Load plain lyric lines from pasted text or the current track's existing lyrics.</li>
          <li>Show the active playback position while audio continues playing.</li>
          <li>Play or pause the active track from inside the timing window.</li>
          <li>Tap the selected lyric line to the current playback position, then advance to the next line.</li>
          <li>Seek back to a timed line for quick checks.</li>
          <li>Nudge all timed lines by <code>0.10s</code> or <code>0.50s</code> to fix drift.</li>
          <li>Copy or export an <code>.lrc</code> file.</li>
        </ul>
        <p>
          When the current track is a local file, export defaults to a matching sidecar path such as
          <code>track-name.lrc</code>. For streamed sources, Spectralis just asks where to put it.
        </p>

        <h3>Lyric explanations</h3>
        <p>
          Genius-style annotations that surface below the current lyric line during playback. They're
          stored as timestamp-keyed JSON and can be embedded two ways.
        </p>
        <p><strong>Method 1 — sidecar .lrc.json file</strong>, next to the .lrc file:</p>
        <CodeBlock>{`track-name.mp3
track-name.lrc        ← synced lyrics
track-name.lrc.json   ← explanations`}</CodeBlock>
        <p>Timestamps use <code>MM:SS.MS</code> (minutes, seconds, centiseconds), mapped to explanation text:</p>
        <CodeBlock>{`{
  "00:12.50": "Opening line sets the scene",
  "00:24.26": "Central metaphor about emptiness",
  "00:31.75": "Glass shards symbolize fragmentation"
}`}</CodeBlock>
        <p>
          <strong>Method 2 — ID3v2 tag embedding.</strong> Using a metadata editor like foobar2000, add
          a custom text frame: type <em>User Text Information Frame (TXXX)</em>, description
          <code>LYRIC_EXPLANATIONS</code>, value the same JSON object as above. This method travels
          with the audio file and wins if both a sidecar and an embedded version exist.
        </p>
        <ul>
          <li>Match timestamps exactly to the LRC file — if the LRC has <code>[00:12.50]</code>, use <code>"00:12.50"</code> in the JSON.</li>
          <li>Keep explanations to one or two sentences.</li>
          <li>Escape special characters in JSON: <code>\"</code> for quotes, <code>\\</code> for backslashes.</li>
          <li>Validate the JSON before embedding it.</li>
        </ul>

        <h3>Content warnings</h3>
        <p>
          Short labels attached to individual local tracks. When a track with warnings is about to
          play, a pre-play popup lists the tags and requires confirmation before audio starts.
        </p>
        <ol>
          <li>Open the queue panel.</li>
          <li>Right-click any local file track.</li>
          <li>Choose <strong>Content Warnings…</strong>.</li>
          <li>Enter labels separated by commas — e.g. <code>violence, flashing lights, loud sounds</code>.</li>
          <li>Click <strong>Save</strong>.</li>
        </ol>
        <p>
          The menu item shows a checkmark suffix when a track already has warnings configured. Every
          time a labeled track is about to play — whether triggered manually, by queue auto-advance, or
          by previous/next navigation — a modal lists the tags as chips with <strong>Play Anyway</strong>
          (dismiss and start) or <strong>Cancel</strong>/Escape (abort without advancing the queue).
        </p>
        <p>Warnings are stored as plain, human-editable JSON, keyed by normalized file path:</p>
        <CodeBlock>{`%LocalAppData%\\Spectralis\\content_warnings.json

{ "c:\\\\path\\\\to\\\\file.mp3": ["tag1", "tag2"] }`}</CodeBlock>
        <p>
          Content warnings apply to <strong>local file tracks only</strong> — Spotify, YouTube,
          SoundCloud, Suno, and shared-queue URL pointers aren't covered, at least not yet.
        </p>
      </>
    ),
    visual: null,
  },
  {
    slug: 'obs-overlay',
    icon: MonitorPlay,
    label: 'Streaming',
    ext: 'localhost:5128',
    title: 'The OBS overlay server',
    summary: 'A local web server with eleven layout presets, pushed live over Server-Sent Events — no polling, no page-reload flash between songs.',
    body: (
      <>
        <p>
          Spectralis runs a tiny local HTTP server the moment it launches — a browser source pointed at
          <code>localhost:5128</code> gets a live, transparent overlay with zero setup. State pushes over
          Server-Sent Events the instant a track changes, so there's no polling interval to tune and no
          page-reload flash between songs.
        </p>

        <h3>Routes</h3>
        <DocTable
          headers={['Route', 'Purpose']}
          rows={[
            [<code key="1">GET /obs/{'{token}'}</code>, 'Self-contained overlay HTML, with all eleven presets available via ?preset=.'],
            [<code key="2">GET /obs/{'{token}'}/state</code>, 'Current overlay state as JSON.'],
            [<code key="3">GET /obs/{'{token}'}/events</code>, 'An SSE stream of state pushes — this is what drives the live overlay.'],
            [<code key="4">GET /obs/{'{token}'}/assets/artwork</code>, 'Current album art bytes (JPEG).'],
            [<code key="5">GET /obs/{'{token}'}/visualizer</code>, 'Current visualizer levels, RMS, and peak as JSON.'],
          ]}
        />

        <h3>Presets</h3>
        <p>Eleven layouts ship in the box, swapped with a single query parameter — no OBS-side CSS editing required:</p>
        <CodeBlock>{`compact (default)     lyrics-lower-third   full-visualizer
queue-sidebar         vertical-stream      capsule-mode
minimal-ticker        album-card           lyrics-focus
visualizer-strip      stage-banner`}</CodeBlock>
        <CodeBlock>{`http://localhost:5128/obs/<token>?preset=lyrics-lower-third
http://localhost:5128/obs/<token>?preset=full-visualizer
http://localhost:5128/obs/<token>?preset=queue-sidebar`}</CodeBlock>

        <h3>Push cadence and the token</h3>
        <p>
          State pushes throttle to 100ms intervals. The <code>{'{token}'}</code> in every route is a
          random GUID generated automatically the first time it's needed — you'll find your overlay's
          full URL, ready to paste into an OBS browser source, in the OBS dialog in Settings, along with
          a button to regenerate the token if a URL ever leaks.
        </p>

        <h3>What the overlay can't leak</h3>
        <p>
          Overlay state is built specifically so it's safe to screen-share: it never contains local file
          paths. Cache-busting for artwork uses an internal version marker instead of exposing the
          actual file path to overlay consumers, and multi-source state (Spotify, SoundCloud, Suno,
          YouTube) follows the same active-engine priority the main player UI uses, so the overlay always
          reflects whatever's actually playing.
        </p>
      </>
    ),
    visual: {
      kind: 'code',
      filename: 'browser source url',
      code: `http://localhost:5128/obs/<token>?preset=lyrics-lower-third
http://localhost:5128/obs/<token>?preset=full-visualizer
http://localhost:5128/obs/<token>?preset=queue-sidebar`,
    },
  },
  {
    slug: 'shared-play',
    icon: Users,
    label: 'Listen Together',
    ext: 'spectralis.app',
    title: 'Shared Play, room by room',
    summary: 'A short room code anyone can join from a browser — synced visualizer, live lyrics, reactions, and a listener-submitted queue.',
    body: (
      <>
        <p>
          Hosting hands out a short room code — <code>X7K-29Q</code> — that anyone can drop into
          <code>spectralis.app</code> from any browser, no account and no app install required to just
          listen along. The listener page shows a synced spinning-disc visualizer, a live spectrum
          meter, current + next lyric lines, and a running listener count with a connection-status dot.
        </p>

        <h3>How a room is packaged</h3>
        <p>
          When hosting starts, Spectralis uploads a package built from the current track. Two variants
          exist side by side: a rich <code>spectralis-rich.zip</code> package for compatible clients —
          preserving embedded metadata, album art, and any embedded visualizer — and a plain
          <code>browser-audio</code> fallback so anyone can listen in an ordinary browser tab with no
          special support needed. A small manifest ties the two together along with the track's synced
          lyrics.
        </p>

        <h3>Playback state</h3>
        <p>
          The host is authoritative: it posts play/pause state and position ticks (rate-limited, tagged
          with a protocol version and the host's own clock) and every listener — browser or another
          Spectralis client — just follows along. Nothing about the sync model requires listeners to
          have accurate clocks of their own.
        </p>

        <h3>Queue</h3>
        <p>
          The host holds the canonical queue and can pre-stage a track's package before it goes live, so
          browser rooms can pre-download it and there's no dead air switching songs. Browser guests can
          submit a single track request — a Spotify link, a YouTube link, or a direct audio URL — which
          the desktop host polls for and resolves into something playable.
        </p>

        <h3>Presence and reactions</h3>
        <p>
          Presence is a lightweight heartbeat: each client periodically checks in with a display name
          and gets back a live listener count and the current participant list. Reactions are short-lived
          room events — a type and a label, each stamped with when it happened — which is what powers the
          floating ❤️ 🔥 😮 ⚡ +1 reaction bar drifting up from the bottom of the listener page.
        </p>

        <h3>Live Channels</h3>
        <p>
          A Live Channel is a permanent link tied to you rather than to one session — it always points
          at whatever Shared Play session you currently have live, so you can put one fixed "listen with
          me" link in a stream panel or bio instead of posting a fresh code every time. Channels track
          aggregate stats too: total seconds shared, total listener-seconds, peak concurrent listeners,
          and per-track play time — a lightweight "what actually landed" view of a session after the fact.
        </p>
        <p>
          Discord Rich Presence publishes what's playing with a one-click "Listen Together" button
          straight from your status — the fastest path from "what's this song" to actually in the room.
        </p>

        <h3>Privacy and expiration</h3>
        <p>
          Shared Play links are private by obscurity, not account authentication — anyone holding the
          link can join until it expires or is removed. Sessions are created with a 12-hour expiration
          and get cleaned up shortly after. Don't share content through Shared Play unless you're
          comfortable with anyone who has the link accessing it for that window.
        </p>
      </>
    ),
    visual: null,
  },
]

export function getArticle(slug) {
  return ARTICLES.find((a) => a.slug === slug)
}
