// "Rasterize" a vector layer — bakes its shapes, at the CURRENT playhead's
// resolved transform, to a plain image and replaces the layer in place with
// a `bitmap` layer holding it. Standard "Rasterize Layer" semantics: trades
// shape/keyframe editability for direct pixel paintability (the Workspace
// canvas's brush tool). Reuses renderLayerAt directly — the exact same
// drawing code the live preview/export use — onto a fresh transparent
// offscreen canvas, so the bake is pixel-identical to what's on screen.
import { renderLayerAt } from '../core/render.js';
import { aspectSize } from '../preview/drawPreview';
import { emptyTracks } from '../state/factories';
import type { ProjectStore } from '../state/project.svelte';
import type { AnyLayer } from '../types/project';

export function rasterizeLayer(store: ProjectStore, layerId: string): void {
  const idx = store.project.layers.findIndex((l) => l.id === layerId);
  const layer = store.project.layers[idx];
  if (idx < 0 || !layer || layer.type !== 'vector') return;

  const { w: W, h: H } = aspectSize(store.project.meta.aspect);
  const canvas = document.createElement('canvas');
  canvas.width = W;
  canvas.height = H;
  const ctx = canvas.getContext('2d');
  if (!ctx) return;
  renderLayerAt(ctx, layer, store.playhead, W, H, performance.now(), 0);
  const dataUrl = canvas.toDataURL('image/png');

  // x:135/y:240 is canvas-center under the SAME `* (W/270)`/`* (H/480)`
  // convention renderLayerAt itself uses (both ratios reduce to exactly
  // half regardless of aspect) — the baked image already has this layer's
  // whole prior transform "burned in", so the new bitmap layer starts at a
  // neutral identity transform instead of inheriting the old one.
  const rasterized: AnyLayer = {
    id: layer.id,
    name: layer.name,
    type: 'bitmap',
    visible: layer.visible,
    locked: layer.locked,
    tracks: emptyTracks(),
    statics: { x: 135, y: 240, scale: 1, rotation: 0, opacity: 1, hueA: 0, hueB: 0 },
    params: { dataUrl, sourceAssetId: null },
    visibleTrack: layer.visibleTrack,
    groupId: layer.groupId,
  };
  store.project.layers.splice(idx, 1, rasterized);
  store.commit();
}
