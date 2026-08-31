export type Ease = 'hold' | 'linear' | 'incubic' | 'outcubic' | 'inoutcubic' | 'insine' | 'outsine';

// Ease lives on the SEGMENT'S START keyframe (outgoing easing) — preserved from the old model.
export interface Keyframe {
  id: string; // stable id (crypto.randomUUID()) — not in the old file format, stripped on export.
  t: number;
  v: number;
  ease: Ease;
}
export type Track = Keyframe[];

// Show/hide keyframes — a boolean can't tween, so this is a separate, simpler
// shape from Keyframe/Track rather than another `ease` value on the numeric
// model. "Hold" semantics only: the layer's visibility at any time is
// whichever keyframe's `v` is most recent at/before that time.
export interface VisibilityKeyframe {
  id: string;
  t: number;
  v: boolean;
}

export const ANIM_KEYS = ['x', 'y', 'scale', 'rotation', 'opacity', 'hueA', 'hueB'] as const;
export type AnimKey = (typeof ANIM_KEYS)[number];
export type Tracks = Record<AnimKey, Track>;
export type Statics = Record<AnimKey, number>;

// A layer is only ever one of these two primitives now — bitmap (raster
// pixels) or vector (editable shapes), the same split every real drawing
// tool is built on. The old fixed "looks" (orb/ring/streak/wheel/
// ambientBeam/shard/text/lyrics) never belonged as layer KINDS — they're
// Assets > Templates now, built out of vector content (src/lib/vectorize.ts
// converts the old per-kind params into real VectorShape[] geometry, reused
// by both the v3 project migration in lib/migrate.ts and the rebuilt
// lib/templates.ts, so migrated saves and fresh templates never drift).
export type LayerType = 'bitmap' | 'vector';

// Still used by the (session-only) raw-LRC-import scratch fields on Project
// below and by lib/lyricsImport.ts's word-splitting — NOT tied to a layer
// kind anymore. The old `lyrics` LayerType is gone; importing timed lyrics
// now generates a group of plain keyframed `vector` text-shape layers (see
// lib/lyricsImport.ts's buildLyricsLayerGroup) instead of one special layer
// that reads from this data at render time.
export interface LyricWord {
  text: string;
  time: number;
  endTime: number;
  isKey: boolean;
  seed: number;
}

// A fill can be flat, or a gradient. '$hueA'/'$hueB' resolve to the layer's
// own hue tracks (already-keyframeable ANIM_KEYS) at render time — lets
// vectorized shapes keep the hue-cycling look the old procedural kinds had,
// with no new animation-engine changes. Any other string is a literal CSS
// color, or the sentinel 'none' meaning "don't paint this" (paired with
// strokeWidth: 0 for strokes — canvas doesn't understand 'none' as a real
// color, so 'none' is checked for explicitly at draw time, never handed to
// ctx.fillStyle/strokeStyle directly).
export type FillColor = string | '$hueA' | '$hueB';
export interface GradientStop {
  offset: number; // 0..1
  color: FillColor;
}
export type Fill =
  | { kind: 'flat'; color: FillColor }
  | { kind: 'radial'; stops: GradientStop[] }
  | { kind: 'linear'; angle: number; stops: GradientStop[] }; // angle in radians, matching the layer rotation track's own unit

export interface Glow {
  blur: number;
  color?: FillColor; // defaults to the shape's fill/stroke color at draw time
}

// No parent-pointer field (mirrors state/nodeWorld.svelte.ts's SceneNode
// convention) — a path shape just owns a flat, ordered point list. Handles
// are stored as offsets FROM the anchor, not absolute coordinates, so moving
// an anchor moves its handles with it for free.
export interface AnchorPoint {
  id: string;
  x: number;
  y: number;
  handleIn: { x: number; y: number } | null;
  handleOut: { x: number; y: number } | null;
  // Whether dragging one handle keeps the other mirrored opposite it (the
  // default, smooth-curve pen-tool behavior) — Alt-drag breaks this per anchor.
  mirrored: boolean;
}

// A small, fixed animatable set for an individual SHAPE inside a vector
// layer — separate from the layer's own ANIM_KEYS (which move/scale/rotate/
// fade the WHOLE layer, every shape in it together). x/y here are a NUDGE
// added on top of the shape's own authored geometry (not a replacement
// position); scale/rotation pivot around the shape's own center (see
// core/shapes.js's boundsOf). No hueA/hueB — a shape's color already animates
// via the layer's existing hueA/hueB tracks when its fill/stroke/glow uses
// the '$hueA'/'$hueB' sentinel, so a second hue track per shape would be
// redundant. Same Track/Ease model as the layer's own tracks (hold = snap,
// every other Ease = a tween) — see state/project.svelte.ts's
// toggleShapeKeyframing/addShapeKeyframeAtPlayhead.
export const SHAPE_ANIM_KEYS = ['x', 'y', 'scale', 'rotation', 'opacity'] as const;
export type ShapeAnimKey = (typeof SHAPE_ANIM_KEYS)[number];
export const SHAPE_ANIM_DEFAULTS: Record<ShapeAnimKey, number> = { x: 0, y: 0, scale: 1, rotation: 0, opacity: 1 };

interface VectorShapeBase {
  id: string;
  fill: Fill;
  // 'none' + strokeWidth: 0 together mean "no stroke" — see FillColor's doc.
  stroke: FillColor;
  strokeWidth: number;
  glow?: Glow;
  // Optional per-shape animation — absent/empty means "render exactly as
  // authored, no per-shape motion", same convention as layer locked/groupId.
  // Each present key's Track takes priority over its matching animStatics
  // entry, exactly like a layer's tracks/statics pair.
  animTracks?: Partial<Record<ShapeAnimKey, Track>>;
  animStatics?: Partial<Record<ShapeAnimKey, number>>;
}
export interface VectorPathShape extends VectorShapeBase {
  kind: 'path';
  closed: boolean;
  points: AnchorPoint[];
}
export interface VectorRectShape extends VectorShapeBase {
  kind: 'rect';
  x: number;
  y: number;
  w: number;
  h: number;
  rotation: number; // radians, about the rect's own center
}
export interface VectorEllipseShape extends VectorShapeBase {
  kind: 'ellipse';
  x: number;
  y: number;
  rx: number;
  ry: number;
  rotation: number; // radians
}
export interface VectorLineShape extends VectorShapeBase {
  kind: 'line';
  x1: number;
  y1: number;
  x2: number;
  y2: number;
}
export interface VectorPolygonShape extends VectorShapeBase {
  kind: 'polygon';
  x: number;
  y: number;
  sides: number;
  radius: number;
  rotation: number; // radians
}
export interface VectorTextShape extends VectorShapeBase {
  kind: 'text';
  x: number;
  y: number;
  text: string;
  fontSize: number;
  tracking: number;
  // Per-glyph wobble amount, same formula the old lyrics-only jitter used —
  // 0 (the default new text shapes get) renders perfectly static, matching
  // what plain `text` layers always looked like pre-vectorization.
  jitter?: number;
}
export type VectorShape = VectorPathShape | VectorRectShape | VectorEllipseShape | VectorLineShape | VectorPolygonShape | VectorTextShape;

export interface LayerParamsByType {
  vector: { shapes: VectorShape[] };
  // Always self-contained: `dataUrl` holds the actual image bytes directly on
  // the layer (part of the ordinary VIZP JSON payload), not just a reference
  // into the session-only, non-persisted AssetLibrary — so a saved project
  // never goes stale/broken if the original asset is later removed or the
  // library is cleared. `sourceAssetId` is purely a soft link back to the
  // AssetLibrary entry it was dragged in from, for editor-side convenience
  // (e.g. "this came from Cover.png") — never required for rendering.
  //
  // Deliberately no width/height here — a bitmap layer is never "a box you
  // define the size of". It always spans the FULL capsule canvas at scale:1
  // (like a normal image-editor layer), stretched to whatever the project's
  // actual aspect is; the generic scale/rotation/position tracks every layer
  // already has are how you resize/move/rotate it afterward, same as vector.
  bitmap: { dataUrl: string | null; sourceAssetId: string | null };
}

export interface Layer<T extends LayerType = LayerType> {
  id: string;
  name: string;
  type: T;
  visible: boolean;
  // Optional (not present in pre-QoL saves) — blocks canvas/timeline selection and
  // dragging while leaving the layer visible and Inspector-editable. Read as
  // `layer.locked ?? false` everywhere so old saves default to unlocked.
  locked?: boolean;
  tracks: Tracks;
  statics: Statics;
  params: LayerParamsByType[T];
  // Optional (not present in pre-QoL saves) — animatable show/hide. Empty or
  // absent means "use the static `visible` flag above", so old saves need no
  // migration, same convention as `locked`. Read via evalVisible() in
  // core/render.js wherever `layer.visible` used to be read directly.
  visibleTrack?: VisibilityKeyframe[];
  // Optional — which LayerGroup (below) this layer belongs to, if any. No
  // recursive tree: a flat array with an optional groupId, same shape layers
  // already have; LayersPanel.svelte clusters consecutive same-groupId rows
  // under a collapsible header. Absent = ungrouped, same convention as locked.
  groupId?: string;
}
// A distributed union (one concrete Layer<T> per branch), NOT Layer<LayerType> —
// the latter collapses `type`/`params` into a single non-discriminated shape, so
// `if (layer.type === 'vector')` wouldn't narrow `layer.params` to the vector shape.
export type AnyLayer = { [K in LayerType]: Layer<K> }[LayerType];

// A named cluster of layers — currently only produced by the Lyrics Importer
// (lib/lyricsImport.ts groups its generated per-line/word/phrase layers under
// one "Lyrics" group) and by v3 migration doing the same for old `lyrics`
// layers, but generic enough for any future multi-layer generator.
export interface LayerGroup {
  id: string;
  name: string;
  collapsed?: boolean;
}

export interface Section {
  id: string;
  label: string;
  start: number;
  end: number;
  hue: number;
  hue2: number;
  intensity: number;
  noLyrics: boolean;
}

export type Aspect = '9x16' | '16x9' | '1x1';

export interface ProjectMeta {
  title: string;
  artist: string;
  slug: string;
  songEnd: number;
  aspect: Aspect;
}

export interface Project {
  formatVersion: 2 | 3;
  meta: ProjectMeta;
  sections: Section[];
  layers: AnyLayer[];
  layerGroups?: LayerGroup[];
  importedWords?: LyricWord[];
  importedLrcRaw?: string;
}
