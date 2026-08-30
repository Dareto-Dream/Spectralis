import { describe, expect, it } from 'vitest';
import {
  resolveLayerTransform,
  screenToLayerLocal,
  layerLocalToScreen,
  layerLocalBounds,
  shapeLocalBounds,
  hitTestShapeLocal,
  pointInLocalBounds,
  newAnchor,
} from '../src/lib/vectorHitTest';
import type { AnyLayer, VectorShape } from '../src/types/project';

function vectorLayer(overrides: Partial<AnyLayer['statics']> = {}): AnyLayer {
  return {
    id: 'l1',
    name: 'L',
    type: 'vector',
    visible: true,
    statics: { x: 135, y: 220, scale: 1, rotation: 0, opacity: 1, hueA: 0, hueB: 0, ...overrides },
    tracks: { x: [], y: [], scale: [], rotation: [], opacity: [], hueA: [], hueB: [] },
    params: { shapes: [{ id: 's1', kind: 'rect', x: -10, y: -10, w: 20, h: 20, rotation: 0, fill: { kind: 'flat', color: '#fff' }, stroke: 'none', strokeWidth: 0 }] },
  };
}

describe('vectorHitTest.ts', () => {
  it('resolveLayerTransform matches the same x/y/rotation/scale math renderLayerAt applies', () => {
    const layer = vectorLayer();
    const tr = resolveLayerTransform(layer, 0, 270, 480);
    expect(tr.x).toBe(135); // 135 * (270/270)
    expect(tr.y).toBe(220); // 220 * (480/480)
    expect(tr.rotation).toBe(0);
  });

  it('screenToLayerLocal / layerLocalToScreen round-trip for an unrotated, unscaled layer', () => {
    const layer = vectorLayer();
    const tr = resolveLayerTransform(layer, 0, 270, 480);
    const local = screenToLayerLocal(150, 230, tr);
    const back = layerLocalToScreen(local.x, local.y, tr);
    expect(back.x).toBeCloseTo(150, 5);
    expect(back.y).toBeCloseTo(230, 5);
  });

  it('screenToLayerLocal / layerLocalToScreen round-trip through rotation + scale', () => {
    const layer = vectorLayer({ rotation: 1.1, scale: 2 });
    const tr = resolveLayerTransform(layer, 0, 270, 480);
    const local = screenToLayerLocal(180, 260, tr);
    const back = layerLocalToScreen(local.x, local.y, tr);
    expect(back.x).toBeCloseTo(180, 5);
    expect(back.y).toBeCloseTo(260, 5);
  });

  it('layerLocalBounds for a vector layer unions every shape\'s bounds (canvas size is irrelevant to it)', () => {
    const layer = vectorLayer();
    const b = layerLocalBounds(layer, 270, 480);
    expect(b).toEqual({ x: -10, y: -10, w: 20, h: 20 });
  });

  it('layerLocalBounds for a bitmap layer is the FULL canvas, centered — it has no authored size of its own', () => {
    const bitmap: AnyLayer = {
      ...vectorLayer(),
      type: 'bitmap',
      params: { dataUrl: null, sourceAssetId: null },
    };
    expect(layerLocalBounds(bitmap, 270, 480)).toEqual({ x: -135, y: -240, w: 270, h: 480 });
  });

  it('resolveLayerTransform gives a bitmap layer plain-scale worldScale (not the minSide/380 shape normalization)', () => {
    const bitmap: AnyLayer = { ...vectorLayer({ scale: 2 }), type: 'bitmap', params: { dataUrl: null, sourceAssetId: null } };
    const tr = resolveLayerTransform(bitmap, 0, 270, 480);
    expect(tr.worldScale).toBe(2);
  });

  it('pointInLocalBounds is inclusive of the edges', () => {
    const bounds = { x: 0, y: 0, w: 10, h: 10 };
    expect(pointInLocalBounds({ x: 0, y: 0 }, bounds)).toBe(true);
    expect(pointInLocalBounds({ x: 10, y: 10 }, bounds)).toBe(true);
    expect(pointInLocalBounds({ x: 11, y: 5 }, bounds)).toBe(false);
  });

  it('newAnchor creates an unhandled, mirrored-by-default point with a fresh id', () => {
    const a = newAnchor(5, 6);
    const b = newAnchor(5, 6);
    expect(a.id).not.toBe(b.id);
    expect(a).toMatchObject({ x: 5, y: 6, handleIn: null, handleOut: null, mirrored: true });
  });

  describe('hitTestShapeLocal / shapeLocalBounds — per-shape click targets (Select tool)', () => {
    const rect: VectorShape = { id: 'r', kind: 'rect', x: 0, y: 0, w: 20, h: 10, rotation: 0, fill: { kind: 'flat', color: '#fff' }, stroke: 'none', strokeWidth: 0 };
    const ellipse: VectorShape = { id: 'e', kind: 'ellipse', x: 0, y: 0, rx: 10, ry: 5, rotation: 0, fill: { kind: 'flat', color: '#fff' }, stroke: 'none', strokeWidth: 0 };
    const line: VectorShape = { id: 'l', kind: 'line', x1: 0, y1: 0, x2: 10, y2: 0, fill: { kind: 'flat', color: '#fff' }, stroke: '#fff', strokeWidth: 2 };
    const polygon: VectorShape = { id: 'p', kind: 'polygon', x: 0, y: 0, sides: 5, radius: 10, rotation: 0, fill: { kind: 'flat', color: '#fff' }, stroke: 'none', strokeWidth: 0 };

    it('rect hit-tests as an axis-aligned box', () => {
      expect(hitTestShapeLocal(rect, { x: 10, y: 5 })).toBe(true);
      expect(hitTestShapeLocal(rect, { x: 25, y: 5 })).toBe(false);
    });

    it('ellipse hit-tests against its actual radii, not its bounding box', () => {
      expect(hitTestShapeLocal(ellipse, { x: 0, y: 0 })).toBe(true);
      // Inside the bbox corner but outside the ellipse itself.
      expect(hitTestShapeLocal(ellipse, { x: 9, y: 4 })).toBe(false);
    });

    it('line hit-tests within a small distance of the segment, not the whole bbox', () => {
      expect(hitTestShapeLocal(line, { x: 5, y: 0 })).toBe(true);
      expect(hitTestShapeLocal(line, { x: 5, y: 20 })).toBe(false);
    });

    it('polygon approximates with a circle of its radius', () => {
      expect(hitTestShapeLocal(polygon, { x: 5, y: 5 })).toBe(true);
      expect(hitTestShapeLocal(polygon, { x: 50, y: 50 })).toBe(false);
    });

    it('two overlapping shapes each only claim their own click area', () => {
      // A click that lands in the rect's bbox corner but outside the smaller
      // ellipse sharing that space should hit the rect, not the ellipse.
      expect(hitTestShapeLocal(rect, { x: 19, y: 9 })).toBe(true);
      expect(hitTestShapeLocal(ellipse, { x: 19, y: 9 })).toBe(false);
    });

    it('shapeLocalBounds matches a single shape, not a union of every shape in the layer', () => {
      expect(shapeLocalBounds(rect)).toEqual({ x: 0, y: 0, w: 20, h: 10 });
    });
  });
});
