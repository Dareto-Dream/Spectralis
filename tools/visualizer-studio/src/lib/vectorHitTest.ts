// Shared screen<->layer-local coordinate math for the Workspace canvas
// (preview/WorkspaceCanvas.svelte) — the same generic transform
// core/render.js's renderLayerAt applies (translate/rotate/scale) before
// drawing a layer's content, inverted so pointer clicks can be turned back
// into the local coordinates shapes/paint strokes are authored in. Pure,
// framework-free, testable in isolation — same pattern as timeline/
// snapping.ts.
import { evalTrack } from '../core/ease.js';
import { boundsOf } from '../core/shapes.js';
import type { AnchorPoint, AnyLayer } from '../types/project';

export interface LayerTransform {
  x: number;
  y: number;
  rotation: number;
  scale: number;
  worldScale: number; // scale * (minSide/380) — what shape-local units actually get multiplied by
}

export function resolveLayerTransform(layer: AnyLayer, t: number, W: number, H: number): LayerTransform {
  const x = evalTrack(layer.tracks.x, t, layer.statics.x) * (W / 270);
  const y = evalTrack(layer.tracks.y, t, layer.statics.y) * (H / 480);
  const rotation = evalTrack(layer.tracks.rotation, t, layer.statics.rotation);
  const scale = evalTrack(layer.tracks.scale, t, layer.statics.scale);
  const minSide = Math.min(W, H);
  return { x, y, rotation, scale, worldScale: scale * (minSide / 380) };
}

export function screenToLayerLocal(px: number, py: number, tr: LayerTransform): { x: number; y: number } {
  const dx = px - tr.x;
  const dy = py - tr.y;
  const cos = Math.cos(-tr.rotation);
  const sin = Math.sin(-tr.rotation);
  const s = tr.worldScale || 1;
  return { x: (dx * cos - dy * sin) / s, y: (dx * sin + dy * cos) / s };
}

export function layerLocalToScreen(lx: number, ly: number, tr: LayerTransform): { x: number; y: number } {
  const s = tr.worldScale || 1;
  const sx = lx * s;
  const sy = ly * s;
  const cos = Math.cos(tr.rotation);
  const sin = Math.sin(tr.rotation);
  return { x: tr.x + (sx * cos - sy * sin), y: tr.y + (sx * sin + sy * cos) };
}

// Local-space (top-left form) bounding box of a layer's own content — a
// bitmap layer's authored box, or the union of every vector shape's bounds.
// Used to draw the Workspace canvas's selection outline/handles, and to
// hit-test "did this click land on this layer at all" for click-to-select.
export function layerLocalBounds(layer: AnyLayer): { x: number; y: number; w: number; h: number } {
  if (layer.type === 'bitmap') {
    return { x: -layer.params.w / 2, y: -layer.params.h / 2, w: layer.params.w, h: layer.params.h };
  }
  const shapes = layer.params.shapes;
  if (!shapes.length) return { x: -20, y: -20, w: 40, h: 40 };
  let minX = Infinity, minY = Infinity, maxX = -Infinity, maxY = -Infinity;
  for (const shape of shapes) {
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    const b = boundsOf(shape as any) as { cx: number; cy: number; w: number; h: number };
    minX = Math.min(minX, b.cx - b.w / 2);
    minY = Math.min(minY, b.cy - b.h / 2);
    maxX = Math.max(maxX, b.cx + b.w / 2);
    maxY = Math.max(maxY, b.cy + b.h / 2);
  }
  return { x: minX, y: minY, w: maxX - minX, h: maxY - minY };
}

export function pointInLocalBounds(local: { x: number; y: number }, bounds: { x: number; y: number; w: number; h: number }): boolean {
  return local.x >= bounds.x && local.x <= bounds.x + bounds.w && local.y >= bounds.y && local.y <= bounds.y + bounds.h;
}

// Straight-line distance in SCREEN space — used to hit-test pen-tool anchors/
// handles against a fixed pixel radius regardless of the layer's own scale.
export function screenDist(ax: number, ay: number, bx: number, by: number): number {
  return Math.hypot(ax - bx, ay - by);
}

export function newAnchor(x: number, y: number): AnchorPoint {
  return { id: crypto.randomUUID(), x, y, handleIn: null, handleOut: null, mirrored: true };
}
