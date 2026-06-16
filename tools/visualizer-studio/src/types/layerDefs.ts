import type { LayerParamsByType, LayerType } from './project';

export interface ParamFieldDef {
  key: string;
  label: string;
  kind: 'number' | 'text' | 'checkbox' | 'select';
  step?: number;
  min?: number;
  max?: number;
  options?: string[];
  placeholder?: string;
}

export interface LayerTypeDef<T extends LayerType> {
  type: T;
  label: string;
  defaultName: string;
  defaultParams: () => LayerParamsByType[T];
  paramFields: ParamFieldDef[];
  hint?: string;
}

export const LAYER_TYPE_DEFS: { [K in LayerType]: LayerTypeDef<K> } = {
  orb: {
    type: 'orb',
    label: 'Orb',
    defaultName: 'Orb',
    defaultParams: () => ({ radius: 60 }),
    paramFields: [{ key: 'radius', label: 'Radius', kind: 'number' }],
  },
  ring: {
    type: 'ring',
    label: 'Ring',
    defaultName: 'Ring',
    defaultParams: () => ({ radius: 80, lineWidth: 2 }),
    paramFields: [
      { key: 'radius', label: 'Radius', kind: 'number' },
      { key: 'lineWidth', label: 'Line Width', kind: 'number', step: 0.5, min: 0.5 },
    ],
  },
  streak: {
    type: 'streak',
    label: 'Streak',
    defaultName: 'Streak',
    defaultParams: () => ({ length: 300, thickness: 14, mono: false }),
    paramFields: [
      { key: 'length', label: 'Length', kind: 'number' },
      { key: 'thickness', label: 'Thickness', kind: 'number' },
      { key: 'mono', label: 'Monochrome', kind: 'checkbox' },
    ],
  },
  wheel: {
    type: 'wheel',
    label: 'Wheel',
    defaultName: 'Wheel',
    defaultParams: () => ({ radius: 90, spokes: 14, spin: 0.35, accentIdx: 0 }),
    paramFields: [
      { key: 'radius', label: 'Radius', kind: 'number' },
      { key: 'spokes', label: 'Spokes', kind: 'number', step: 1, min: 2 },
      { key: 'spin', label: 'Spin Rate', kind: 'number', step: 0.05 },
      { key: 'accentIdx', label: 'Accent Spoke', kind: 'number', step: 1, min: 0 },
    ],
  },
  ambientBeam: {
    type: 'ambientBeam',
    label: 'Ambient Beam',
    defaultName: 'Ambient Beam',
    defaultParams: () => ({ bandHeight: 120 }),
    paramFields: [{ key: 'bandHeight', label: 'Band Height', kind: 'number' }],
  },
  shard: {
    type: 'shard',
    label: 'Shard',
    defaultName: 'Shard',
    defaultParams: () => ({ size: 10, spin: 0.6 }),
    paramFields: [
      { key: 'size', label: 'Size', kind: 'number' },
      { key: 'spin', label: 'Spin Rate', kind: 'number', step: 0.05 },
    ],
  },
  text: {
    type: 'text',
    label: 'Text',
    defaultName: 'Text',
    defaultParams: () => ({ text: 'TEXT', fontSize: 48, tracking: 2 }),
    paramFields: [
      { key: 'text', label: 'Text', kind: 'text' },
      { key: 'fontSize', label: 'Font Size', kind: 'number' },
      { key: 'tracking', label: 'Tracking', kind: 'number', step: 0.5 },
    ],
  },
  lyrics: {
    type: 'lyrics',
    label: 'Lyrics (from LRC)',
    defaultName: 'Lyrics',
    defaultParams: () => ({
      placement: 'ANCHORED',
      fontSize: 64,
      tracking: 2,
      keyWords: '',
      suppressOnNoLyricsSections: true,
      words: [],
    }),
    paramFields: [
      {
        key: 'placement',
        label: 'Placement',
        kind: 'select',
        options: ['ANCHORED', 'ARC_RISE', 'SCATTER', 'STACKED_ECHO', 'SETTLE_FADE'],
      },
      { key: 'fontSize', label: 'Font Size', kind: 'number' },
      { key: 'tracking', label: 'Tracking', kind: 'number', step: 0.5 },
      { key: 'keyWords', label: 'Key Words', kind: 'text', placeholder: 'comma or space separated' },
      { key: 'suppressOnNoLyricsSections', label: 'Suppress on no-lyrics sections', kind: 'checkbox' },
    ],
    hint: 'Only ANCHORED currently renders in the export driver; other placements are reserved.',
  },
};

export const LAYER_TYPES = Object.keys(LAYER_TYPE_DEFS) as LayerType[];
