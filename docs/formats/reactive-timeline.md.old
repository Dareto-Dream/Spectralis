# Reactive Timeline Sidecar (`.spectralis-reactive.json`)

Sidecar files attach track-reactive metadata to any local audio file without modifying it.
Inside `.spectralis` capsules, `reactive.json` at the ZIP root is used instead of a sidecar.

---

## Loading

`Form1.Reactive.cs` checks for `<audiopath-without-extension>.spectralis-reactive.json` every
time a local file loads. Inside capsules, `reactive.json` at the ZIP root is used instead.

The runtime is in `Reactive/ReactiveRuntime.cs`. It is advanced by `AdvanceReactive()` in the
timer tick and seeks via `SeekReactive()` on seek bar moves.

---

## Document Format

Top-level fields (see `ReactiveModels.cs`):

| Field | Required | Description |
|---|---|---|
| `format` | Yes | Must be `"spectralis-track-reactive"` |
| `formatVersion` | Yes | Must be `3` |
| `sections` | No | Named time ranges with mood label (used by OBS overlay) |
| `timeline` | No | List of events at specific timestamps |
| `assets` | No | Referenced resources (future use) |
| `shaderPacks` | No | Referenced shader packs (future use) |

### Sections

Named, non-overlapping time ranges. The first match wins when looking up the current section.
Used by the OBS overlay to populate the section/mood display.

```json
"sections": [
  { "start": 0.0, "end": 32.0, "name": "intro", "mood": "ambient" },
  { "start": 32.0, "end": 96.0, "name": "verse", "mood": "building" }
]
```

### Timeline Events

Each event fires at a specific timestamp and targets a subsystem:

```json
"timeline": [
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
]
```

**Allowed targets:** `theme`, `visualizer`, `lyrics`, `shader`

**Allowed actions:** `set`, `transition`, `reset`

**Easing options:** `linear`, `incubic`, `outcubic`, `inoutcubic`, `insine`, `outsine`

- Numeric params interpolate over `duration` seconds.
- Non-numeric params snap at `t >= 1.0`.

---

## Runtime

`ReactiveRuntime` fires `SectionChanged` and `ParamsChanged` events as the playback position
advances through the timeline.

- The runtime is stateless between loads — `Load(null)` resets it completely.
- Transitions use the easing function for `duration` seconds; numeric params interpolate.
- Events are ordered by `time`; the runtime does a forward scan each tick.

---

## Rules

- The sidecar file must sit next to the audio file with the same base name.
- Inside a capsule, the file must be named `reactive.json` at the ZIP root.
- `format` and `formatVersion` are validated on load; mismatches are silently ignored.
- Invalid event targets or actions are skipped without error.
