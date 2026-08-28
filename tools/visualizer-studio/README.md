# Visualizer Studio

AE-style layer/keyframe/curve editor for building Spectralis capsule audio
visualizers, plus the World (album experience) and Story (narrative page)
tab builders. Rewrite of `metadata/tools/visualizer-studio.html` in
Svelte 5 + dockview — that original file is untouched and stays around as
the parity reference.

## Dev

```
npm install
npm run dev      # http://localhost:5173
npm run check    # svelte-check, 0 errors expected
npm run test     # vitest
```

## Build & deploy

```
npm run build
```

Produces a single self-contained `dist/index.html` (JS/CSS/assets all
inlined via `vite-plugin-singlefile`) — double-click it, no server needed.
That's the whole deployment: copy `dist/index.html` wherever, open it in a
browser. Re-run `npm run build` any time source changes and redistribute
the new file.

## Desktop build (Electron)

Same app, packaged as a native Windows/Linux/macOS app via Electron. The
Electron shell just loads the same `dist/index.html` the web build produces,
plus a `window.native` bridge (see `electron/preload.ts`) that lets asset/
audio/export code skip the browser's base64-in-memory ceiling when it's
available — every native code path has a browser fallback, so the plain web
build behaves identically to before.

```
npm run electron:dev          # hot-reloading dev window against the vite dev server
npm run electron:build:win    # NSIS installer + portable exe -> release/
npm run electron:build:linux  # AppImage -> release/
npm run electron:build:mac    # dmg + zip -> release/
```

Cross-compiling AppImage/dmg from Windows isn't reliable — build those on
(or via CI on) their native OS.

## What it does

- **Studio** tab: layers (orb/ring/streak/wheel/ambientBeam/shard/text/
  lyrics), keyframed animation tracks with a real curve editor, section-based
  color/intensity timeline, undo/redo, multi-select + marquee + snapping,
  autosave + session recovery. "Export Capsule" produces the 5 files a
  `.spectralis` capsule needs (`*_visualizer.html`, `*_module.json`,
  `*_manifest.json`, `*_reactive.json`, `pack_*.py`) — the exported HTML is
  itself a single inline `<script>` page (no ES modules), so it also opens
  standalone via `file://` with no server, same as this app's own build.
- **World** / **Story** tabs: independent generators for the album-experience
  and narrative-page capsule formats. Output via copy-to-clipboard/download,
  not wired into Studio's project state.

See `docs/formats/spectralis-capsule.md`, `spectral-album-world.md`, and
`reactive-timeline.md` at the repo root for what the C# WebView2 player
actually expects from these outputs.

## Keyboard shortcuts

Press `?` in the Studio tab for the full list.
