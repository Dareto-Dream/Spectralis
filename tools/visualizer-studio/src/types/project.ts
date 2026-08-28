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

export type LayerType = 'orb' | 'ring' | 'streak' | 'wheel' | 'ambientBeam' | 'shard' | 'text' | 'lyrics';

export interface LyricWord {
  text: string;
  time: number;
  endTime: number;
  isKey: boolean;
  seed: number;
}

export interface LayerParamsByType {
  orb: { radius: number };
  ring: { radius: number; lineWidth: number };
  streak: { length: number; thickness: number; mono: boolean };
  wheel: { radius: number; spokes: number; spin: number; accentIdx: number };
  ambientBeam: { bandHeight: number };
  shard: { size: number; spin: number };
  text: { text: string; fontSize: number; tracking: number };
  lyrics: {
    // Only ANCHORED currently renders in the export driver; the rest are reserved placements.
    placement: 'ANCHORED' | 'ARC_RISE' | 'SCATTER' | 'STACKED_ECHO' | 'SETTLE_FADE';
    fontSize: number;
    tracking: number;
    keyWords: string;
    suppressOnNoLyricsSections: boolean;
    words: LyricWord[];
  };
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
}
// A distributed union (one concrete Layer<T> per branch), NOT Layer<LayerType> —
// the latter collapses `type`/`params` into a single non-discriminated shape, so
// `if (layer.type === 'lyrics')` wouldn't narrow `layer.params` to the lyrics shape.
export type AnyLayer = { [K in LayerType]: Layer<K> }[LayerType];

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
  formatVersion: 2;
  meta: ProjectMeta;
  sections: Section[];
  layers: AnyLayer[];
  importedWords?: LyricWord[];
  importedLrcRaw?: string;
}
