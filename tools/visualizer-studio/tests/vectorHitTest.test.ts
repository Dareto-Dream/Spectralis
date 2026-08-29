import { describe, expect, it } from 'vitest';
import { resolveLayerTransform, screenToLayerLocal, layerLocalToScreen, layerLocalBounds, pointInLocalBounds, newAnchor } from '../src/lib/vectorHitTest';
import type { AnyLayer } from '../src/types/project';

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
});
