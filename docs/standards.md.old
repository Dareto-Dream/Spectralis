# Spectralis Extension Standards

This document defines the expected patterns for extending Spectralis without reintroducing
hardcoded lists, giant files, or theme drift. It covers visualizers, themes, settings, controls,
form partials, and the OBS overlay system.

For data format specifications, see [formats/](formats/).

---

## Core Rules

- Prefer registries over duplicated lists. If something is user-selectable, there should be one
  source of truth for the available options.
- Prefer data flow over control coupling. Renderers and controls should consume models/state,
  not query the form or audio engine directly.
- Keep renderers stateless. `VisualizerCatalog` stores renderer instances once, so renderer
  classes must not keep mutable per-track or per-frame state.
- Keep theme values centralized. Runtime colors should come from `ThemePalette`,
  `VisualizerTheme`, or a dedicated theme helper, not scattered `Color.FromArgb(...)` calls
  in forms.
- Keep handwritten files focused. If a file starts mixing multiple concerns, split it before it
  becomes the next mega file.
- Generated files are the exception. `*.Designer.cs` may be large; handwritten files should not.
- Embedded modules must run in a complete sandbox with zero access to host system, network, or
  application state.

---

## File Size and Organization

- Target less than 250 lines for most handwritten files.
- If a handwritten file passes roughly 350–400 lines and contains more than one concern, split it.
- Do not put new nested utility/rendering classes inside `Form1` unless they are truly form-private
  and tiny.

Use partial form files by responsibility:

| Partial file | Responsibility |
|---|---|
| `Form1.cs` | Core state and construction |
| `Form1.Events.cs` | Event handlers |
| `Form1.Playback.cs` | Playback and file actions |
| `Form1.Settings.cs` | Settings plumbing |
| `Form1.Theme.cs` | Theme application |
| `Form1.UI.cs` | UI state updates |
| `Form1.Artwork.cs` | Album art logic |
| `Form1.Capsule.cs` | `.spectralis` capsule open/trust/load/dispose |
| `Form1.AlbumWorld.cs` | `.spectral` album world open/trust/load/dispose |
| `Form1.Reactive.cs` | Reactive timeline sidecar load and tick advance |
| `Form1.Obs.cs` | OBS overlay state push |

Use standalone files for reusable systems:

| Naming pattern | Purpose |
|---|---|
| `*Catalog.cs` | Registration/source-of-truth lists |
| `*Store.cs` | Persistence |
| `*Renderer.cs` | A single visualizer implementation |
| `*Theme*.cs` | Shared theme helpers |

---

## Adding a Visualizer

### Required Files

At minimum, a new visualizer touches:

1. `VisualizerMode.cs`
2. A new renderer file such as `PulseRingVisualizerRenderer.cs`
3. `VisualizerCatalog.cs`

### Required Format

1. Add a new enum member to `VisualizerMode`.
2. Create one renderer class per visualizer.
3. Implement `IVisualizerRenderer`.
4. If the renderer needs shared background/HUD helpers, inherit from `VisualizerRendererBase`.
5. Register the visualizer exactly once in `VisualizerCatalog.Definitions`.

Minimal pattern:

```csharp
internal sealed class PulseRingVisualizerRenderer : VisualizerRendererBase, IVisualizerRenderer
{
    public void Draw(Graphics graphics, Rectangle bounds, VisualizerScene scene)
    {
        DrawBackground(graphics, bounds, scene.Theme);
        DrawHud(graphics, bounds, scene);

        // Custom rendering here.
    }
}
```

Register it:

```csharp
new(VisualizerMode.PulseRing, "Pulse Ring", new PulseRingVisualizerRenderer())
```

### Album-Art-Dependent Visualizers

If a visualizer requires album art:

- Set `RequiresAlbumArt: true` in `VisualizerCatalog`
- Do not add custom fallback logic in `Form1`
- Let `VisualizerCatalog.GetOptions(...)` and `GetPreferredMode(...)` handle availability

This keeps the main form dropdown, the settings dialog, and auto-cycle behavior in sync automatically.

### If a Visualizer Needs New Data

Do not let the renderer reach into `AudioEngine`, `Form1`, or control internals. Instead:

1. Add the new field to `VisualizerScene`
2. Populate it in `SpectrumVisualizerControl.CreateScene(...)`
3. Update any smoothing/state handling in `SpectrumVisualizerControl.UpdateFrame(...)`
4. Consume that field inside the renderer

If the new visualizer needs new theme colors:

1. Add them to `VisualizerTheme`
2. Populate them in `SpectrumVisualizerControl.ApplyTheme(...)`
3. Read them from `scene.Theme`

Do not hardcode special renderer-only colors unless they are purely local math derived from the theme.

### Visualizer Anti-Patterns

Do not:

- Add `switch` logic back into `SpectrumVisualizerControl.OnPaint`
- Keep a second visualizer label list in `Form1` or `SettingsDialog`
- Store mutable animation state inside renderer instances
- Query the form from renderer code

---

## Adding a Theme Accent or Theme Mode

### Source of Truth

Theme availability is defined by:

- `ThemeMode` in `AppSettings.cs`
- `ThemeAccent` in `AppSettings.cs`
- `ThemePalette.Create(...)`
- `SettingsDialog.PopulateComboOptions(...)`

### Adding a New Accent

1. Add the enum member to `ThemeAccent`
2. Add the accent colors in `ThemePalette.GetAccentColors(...)`
3. Add the selection option in `SettingsDialog.PopulateComboOptions(...)`
4. Verify `AppSettingsStore.Normalize(...)` still handles invalid enum values correctly
5. Verify contrast still works through `GetReadableTextColor(...)`

### Adding a New Theme Mode

1. Add the enum member to `ThemeMode`
2. Update `ThemePalette.Create(...)` so every palette property is defined for that mode
3. Verify all theme-driven controls still look correct through `ThemeControlStyler` and per-control `ApplyTheme(...)`
4. Verify `WindowChromeStyler.ApplyTheme(...)` still produces acceptable native window chrome
5. Add the mode to `SettingsDialog.PopulateComboOptions(...)`

### Theme Rules

- New controls should consume a `ThemePalette`, not invent their own palette format unless they
  are a full subsystem like the visualizer.
- If a complex surface needs its own derived colors, compute them from `ThemePalette`.
- Do not use `Color.White`, `SystemColors`, or default WinForms control colors for runtime
  surfaces that should be themed.
- If a native popup or form chrome ignores your colors, fix the host/native surface too.

---

## Adding a New Persisted Setting

For any setting that survives app restarts, update all of these layers:

1. `AppSettings`
2. `AppSettings.Clone()`
3. `AppSettingsStore.Normalize(...)`
4. `SettingsDialog`
5. `Form1.Settings.cs` or the subsystem that applies the setting

### Expected Flow

- `AppSettings` defines the property and default value
- `AppSettingsStore` normalizes persisted input
- `SettingsDialog` edits the value
- `Form1` or the owning system applies the value at runtime

### UI Option Lists

If the setting is selected from a dropdown:

- Use `SelectionOption<T>`
- Keep the list in one helper method or catalog
- Do not duplicate the same option list in multiple screens

Examples already following this pattern: `GetSampleRateOptions()`, `GetCycleDurationOptions()`,
`VisualizerCatalog.GetOptions(...)`.

### Setting Anti-Patterns

Do not:

- Read/write the JSON settings file outside `AppSettingsStore`
- Add a new setting to the dialog without applying it at runtime
- Add runtime-only magic numbers without deciding whether they should be user-configurable

---

## Per-Track Data Stores

Some data is keyed by individual track path rather than being a single global setting.
Use a dedicated `*Store.cs` class rather than embedding a `Dictionary<string, T>` inside
`AppSettings`.

### Pattern

```csharp
internal static class TrackContentWarningStore
{
    // Stored at %LocalAppData%\Spectralis\<name>.json
    // Key: normalized (lowercased, fully qualified) file path
    // In-memory cache; only hits disk on writes

    public static T Get(string filePath);
    public static void Set(string filePath, T value);
    public static void Clear(string filePath);
}
```

### Rules

- Normalize paths with `Path.GetFullPath(path).ToLowerInvariant()` before use as keys.
- Keep an in-memory cache; reload from disk only on cold start.
- Write through synchronously on every mutation — these files are small.
- The store is the only code that reads or writes its JSON file.
- Do not embed per-track dictionaries in `AppSettings`; `AppSettings` is for session-level
  preferences, not per-file data.

### Existing Stores

| Store class | File | Keyed by | Purpose |
|---|---|---|---|
| `TrackContentWarningStore` | `content_warnings.json` | File path | TW tags shown before playback |

### Queue Context Menu Integration

Per-track data that requires user editing is surfaced through the queue right-click context menu:

1. Declare `ctxQueue*` field in `Form1.Designer.cs` and initialize it alongside the other
   queue context menu items.
2. Enable the item only for local files (`!IsSharedQueuePointer` and `File.Exists`).
3. Update the item label to reflect current state (e.g. a **✓** suffix when data is present).
4. Open a themed dialog (`*EditDialog.cs`) on click; the dialog writes directly to the store.

---

## Adding or Updating Themed Controls

### Simple Controls

If a control can be themed through shared properties, use `ThemeControlStyler`:

- `ThemeControlStyler.ApplyComboBoxTheme(...)`
- `ThemeControlStyler.ApplyPrimaryButtonTheme(...)`
- `ThemeControlStyler.ApplyGhostButtonTheme(...)`
- `ThemeControlStyler.ApplySliderTheme(...)`
- `ThemeControlStyler.ApplyCheckBoxTheme(...)`

### Complex Controls

If a control has its own drawing system, give it an `ApplyTheme(ThemePalette palette)` method.

Current examples: `SpectrumVisualizerControl.ApplyTheme(...)`, `LyricsViewControl.ApplyTheme(...)`.

### Native Surfaces

If the white/default Windows look leaks through:

- Theme the control itself
- Theme the popup/native child window if one exists
- Theme the form chrome if the surface is a window/dialog

Do not stop after theming only the managed child controls.

---

## Form Standards

- New form behavior should go into the matching partial, not into `Form1.cs` by default.
- If a new concern does not fit an existing partial cleanly, create a new partial file.
- Keep business rules and option catalogs outside the form when possible.
- `Form1` should consume systems such as catalogs, renderers, settings stores, and theme helpers;
  it should not become the home for those systems.

---

## OBS Overlay

`ObsOverlayServer` (`Obs/ObsOverlayServer.cs`) serves a local HTTP endpoint on
`http://127.0.0.1:{port}/obs/{token}`.

### Routes

| Route | Purpose |
|---|---|
| `GET /obs/{token}` | Self-contained overlay HTML (eleven CSS presets via `?preset=`) |
| `GET /obs/{token}/state` | Current `ObsOverlayState` as JSON |
| `GET /obs/{token}/events` | SSE stream of state pushes |
| `GET /obs/{token}/assets/artwork` | Album art bytes (JPEG) |
| `GET /obs/{token}/visualizer` | Current visualizer levels/rms/peak as JSON |

### Presets

`compact` (default), `lyrics-lower-third`, `full-visualizer`, `queue-sidebar`,
`vertical-stream`, `capsule-mode`, `minimal-ticker`, `album-card`, `lyrics-focus`,
`visualizer-strip`, `stage-banner`. Selected via `?preset=<name>`.

### Push Cadence

`PushObsState()` is called from `PulseObs()` which throttles to 100 ms intervals in the timer
tick. Token is a `Guid.NewGuid().ToString("N")` auto-generated in `AppSettings.Normalize` if empty.

### Rules

- State DTOs (`ObsOverlayState` and nested types) must never contain local file paths.
- `ArtworkVersion` is set to the track's `FilePath` for cache-busting without exposing the path
  to overlay consumers.
- Multi-source state follows the same `IsSpotifyActive` / `IsSoundCloudActive` / `IsSunoActive` /
  `IsYouTubeActive` / engine priority as `Form1.UI.cs`.

---

## Review Checklist

Before finishing a feature like a new visualizer, theme, or setting, verify:

- There is one source of truth for the option list
- The feature is available in the settings UI if it is user-configurable
- Runtime behavior reads from normalized settings
- Theme colors come from the palette/system, not ad hoc colors
- No handwritten file became oversized or mixed unrelated concerns
- `dotnet build` succeeds
