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
  // Empty for both kinds — bitmap/vector have no generic scalar fields worth
  // a label+input row; editing happens on the Workspace canvas (shape tools,
  // pen tool, paintbrush) via the left Tools docker, not here. ParamFields.svelte
  // renders `hint` alone when this is empty.
  paramFields: ParamFieldDef[];
  hint?: string;
}

export const LAYER_TYPE_DEFS: { [K in LayerType]: LayerTypeDef<K> } = {
  vector: {
    type: 'vector',
    label: 'Vector',
    defaultName: 'Vector Layer',
    defaultParams: () => ({ shapes: [] }),
    paramFields: [],
    hint: 'Draw shapes on the Workspace canvas using the Tools docker.',
  },
  bitmap: {
    type: 'bitmap',
    label: 'Image',
    defaultName: 'Image Layer',
    defaultParams: () => ({ dataUrl: null, sourceAssetId: null, w: 200, h: 200 }),
    paramFields: [],
    hint: 'Drag an image from Assets onto this layer, or paint directly with the brush tool.',
  },
};

export const LAYER_TYPES = Object.keys(LAYER_TYPE_DEFS) as LayerType[];
