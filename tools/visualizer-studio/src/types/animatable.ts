// Registry of every property this app knows how to keyframe. Today's 7
// ANIM_KEYS (numeric, tweened via ease curves) plus `visible` (boolean, hold
// semantics only) are both listed here — but `Tracks`/`AnimKey` in
// types/project.ts stay exactly as they are for the numeric keys rather than
// being generalized into a `Record<string, Track>` this pass; that's real
// risk to the hot interpolation path (core/render.js, the timeline canvas)
// for no payoff yet with only one boolean property to show for it.
//
// What this registry IS for: the seam later phases (per-layer-type params,
// shape-morph targets on future node/asset layers) plug new animatable
// properties into, instead of growing the AnimKey union again each time.
import { ANIM_KEYS } from './project';

export type AnimatableKind = 'number' | 'boolean';

export interface AnimatablePropDef {
  key: string;
  kind: AnimatableKind;
  label: string;
}

export const ANIMATABLE_PROPS: Record<string, AnimatablePropDef> = {
  x: { key: 'x', kind: 'number', label: 'X' },
  y: { key: 'y', kind: 'number', label: 'Y' },
  scale: { key: 'scale', kind: 'number', label: 'Scale' },
  rotation: { key: 'rotation', kind: 'number', label: 'Rotation' },
  opacity: { key: 'opacity', kind: 'number', label: 'Opacity' },
  hueA: { key: 'hueA', kind: 'number', label: 'Hue A' },
  hueB: { key: 'hueB', kind: 'number', label: 'Hue B' },
  visible: { key: 'visible', kind: 'boolean', label: 'Visibility' },
};

// Defensive: keeps this registry from silently drifting out of sync with
// types/project.ts's ANIM_KEYS if that tuple ever changes.
for (const key of ANIM_KEYS) {
  if (!(key in ANIMATABLE_PROPS) || ANIMATABLE_PROPS[key].kind !== 'number') {
    throw new Error(`animatable.ts registry is out of sync with ANIM_KEYS: "${key}"`);
  }
}
