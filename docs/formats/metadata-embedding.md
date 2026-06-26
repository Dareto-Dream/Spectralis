# Metadata Embedding — ID3v2 Embedded Modules

Embedded modules allow creators to ship rich animated experiences directly inside MP3 files as
portable, self-contained content. They are stored in ID3v2 `TXXX` frames and loaded into
isolated runtime contexts inside Spectralis.

For the full metadata packing standard and tool documentation, see
[metadata/STANDARDS.md](../../metadata/STANDARDS.md).

---

## Module Types

| `type` value | `runtime` value | What it does |
|---|---|---|
| `"visualizer"` | `"wasm"` | WASM binary visualizer |
| `"html"` | `"html"` | Rich HTML overlay or fullscreen experience |
| `"markdown"` | `"markdown"` | Formatted text (liner notes, lyrics) |
| `"video"` | `"h264"` / `"vp9"` / `"av1"` / `"h265"` | Embedded video synced to audio |

---

## Module Definition (DELTA_MODULE_ frame)

Stored in an ID3v2 `TXXX` frame with description prefix `DELTA_MODULE_`:

```json
{
  "id": "my_visualizer",
  "type": "visualizer",
  "runtime": "wasm",
  "entry": "_start",
  "version": "1.0.0",
  "binaryRef": "my_visualizer_wasm",
  "dataRefs": {
    "config": "config_json",
    "theme": "theme_json"
  }
}
```

**Required fields:**

| Field | Description |
|---|---|
| `id` | Unique identifier (alphanumeric, dash, underscore; max 64 chars) |
| `type` | Module type: `visualizer`, `html`, `markdown`, `video` |
| `runtime` | Type specifier: `wasm`, `html`, `markdown`, `h264`, `vp9`, `av1`, `h265` |
| `entry` | WASM: export name; HTML/Markdown: omit; Video: codec string |
| `binaryRef` | ID of a `DELTA_BIN_` frame containing the module's binary |

**Optional fields:**

| Field | Description |
|---|---|
| `version` | Semantic version string |
| `dataRefs` | Object mapping binding names to `DELTA_DATA_` block IDs |
| `width`, `height` | For HTML/Video: pixel dimensions |
| `autoplay` | For video: play on load (default: false) |
| `loop` | For video: repeat (default: true) |

---

## Data Blocks (DELTA_DATA_ frame)

Configuration, theme overrides, and text assets stored in `TXXX` frames:

```
Description: DELTA_DATA_config_json
Text:        { "color": "#FF00AA", "thickness": 2.5 }
```

- Frame encoding: UTF-8.
- Content treated as JSON if it parses; otherwise raw text.
- Maximum size: **64 KB** per block.
- Accessed via `context.GetDataByBinding("bindingName")` in WASM or `delta-data-json:bindingRef`
  in HTML.

---

## Binary Assets (DELTA_BIN_ frame)

Binary data base64-encoded in `TXXX` frames:

```
Description: DELTA_BIN_my_visualizer_wasm
Text:        <base64-encoded WASM binary>
```

- Must be valid base64.
- Decoded size: ≤ **256 KB** (enforced at load time; video: ≤ **16 MB**).
- Supported types: WASM (`.wasm`), images (PNG, JPEG, WebP), video (MP4, WebM, MKV).
- Images referenced from HTML can use `delta-asset:<bindingName>` or `delta-bin:<binaryId>`;
  the player inlines them as `data:` URIs inside the sandbox.

---

## WASM Visualizer

### Sandbox Constraints

WASM modules run in a zero-capability sandbox:

- No filesystem, network, process, or OS API access.
- No host bindings beyond the allowed imports below.
- Linear memory is isolated; no cross-module access.
- No eval or dynamic code generation.
- **Timeout:** 500 ms per frame; exceeded frames are dropped.
- **Deterministic:** no real time, no real randomness, no external state.

### Allowed Imports

```
env.audio_sample   — read a single audio sample (index → float32)
env.random_uint32  — pseudo-random number (seeded by frame, not real randomness)
env.time_ms        — elapsed milliseconds since track start
```

No other imports are resolved. Modules that attempt to import restricted functions fail validation.

### Drawing Instruction Set

All coordinates are normalized to `[0, 1]` relative to the canvas.

**Line:** `X1, Y1, X2, Y2, Color, Thickness (1–20 px)`

**Rectangle:** `X, Y, Width, Height, Color, Thickness, Filled`

**Circle:** `CenterX, CenterY, Radius (0–1), Color, Thickness, Filled`

**Planned in v1.1:** Arc, Bezier, Polygon, Path, Text, Image, Gradient fills.

Colors are ARGB: HTML hex (`#RRGGBB`, `#AARRGGBB`), integer ARGB, or named .NET colors.

### Configuration Pattern

```csharp
var config = context.GetDataByBinding("config");
var strokeColor = TryReadColor(config?.TryGetString("color", "strokeColor"))
    ?? Color.FromArgb(255, 0, 255, 170);
var thickness = config is not null && config.TryGetNumber("thickness", out var t)
    ? Math.Clamp(t, 1f, 10f)
    : 2.2f;
```

Always provide sensible defaults. Clamp numeric values. Fall back gracefully on missing data.

---

## HTML Module

HTML modules embed styled web content rendered in WebView2.

- Binary: UTF-8 HTML referenced by `binaryRef`.
- Maximum total size: **512 KB** HTML + CSS + inline SVG; **2 MB** all assets combined.
- Rendered as a fullscreen overlay or alongside the visualizer.

### What HTML can do

- DOM manipulation (sandboxed; no access to parent window).
- Canvas and SVG rendering.
- CSS animations and transitions.
- `requestAnimationFrame` loops.
- JavaScript limited to sandboxed `window`, `document`, `console`.

### What HTML cannot do

- Access `parent`, `top`, `window.opener`, or native APIs.
- Make network requests (XHR/fetch only resolve `data:` URIs).
- Load external scripts or stylesheets.
- Use `eval` or dynamic script loading.
- Access `localStorage` shared with the app (it is isolated per module).

### Forbidden HTML elements

`script` (managed separately), `iframe`, `object`, `embed`, `applet`, any `on*` event attributes,
`form`, `input[type=file]`, `input[type=submit]`.

### CSS restrictions

Forbidden: `position: fixed`, `position: absolute` (can escape bounds), `z-index > 1000`,
`@import` URLs, `url()` with non-data URIs. Inline styles stripped; use `<style>` or a
`style` data block.

### Asset references in HTML

Binary image assets bound through `dataRefs` can be referenced as:
- `delta-asset:<bindingName>` — resolved to a `data:` URI by the player
- `delta-bin:<binaryId>` — direct binary reference

---

## Markdown Module

- Binary: UTF-8 Markdown (CommonMark dialect) referenced by `binaryRef`.
- Maximum size: **256 KB**.
- Converted to HTML and rendered in a sandboxed WebView2 iframe.
- Supported: headings, paragraphs, bold/italic, lists, code blocks, blockquotes, tables,
  links (HTTP/HTTPS only), inline images (embedded assets only), horizontal rules.
- Forbidden: raw HTML blocks, footnotes, `javascript://` or `data://` links.

---

## Video Module

- Binary: video data referenced by `binaryRef`.
- Maximum size: **16 MB**.
- Supported codecs: H.264 (`h264`), VP9 (`vp9`), AV1 (`av1`), H.265 (`h265`).
- Supported containers: MP4, WebM, MKV.
- Audio is the sync source; video follows. If video is shorter than audio, it loops.
- Seek in audio triggers seek in video. Loop in audio triggers loop in video.
- Frames are updated only on audio frame boundaries.

---

## Load-Time Validation

All module types:
1. Module JSON parses successfully.
2. `type` is one of the known values (case-insensitive).
3. `binaryRef` points to an existing binary asset.
4. All data block sizes are within limits.

WASM-specific:
- `entry` export exists and is callable.
- Binary is valid WASM.
- Module size ≤ 512 KB compiled.

HTML-specific:
- Binary is valid UTF-8; forbidden elements stripped at parse time.

Video-specific:
- Container detected; codec matches `runtime` field.

If any check fails, the embedded module is silently skipped and the track plays with defaults.

---

## Failure Modes

| Failure | Behavior |
|---|---|
| WASM trap (divide by zero, OOB memory) | Frame marked invalid; previous frame re-displayed |
| WASM timeout (>500 ms) | Frame abandoned; module flagged for future skipping |
| HTML script injection attempt | Offending element removed; rendering continues |
| Video corrupted frame | Frame dropped; last valid frame displayed |
| Video decoder lag | Playback catches up; no audio skipping |
| Any rendering error | Instruction skipped; rendering continues |
