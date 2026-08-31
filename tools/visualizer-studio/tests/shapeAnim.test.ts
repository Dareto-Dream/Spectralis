import { describe, expect, it } from 'vitest';
import { drawVectorShape } from '../src/core/shapes.js';
import type { VectorShape } from '../src/types/project';

// A minimal recording canvas context — no jsdom `canvas` package installed
// (see project convention noted elsewhere for DOM-canvas-dependent code), so
// this exercises drawVectorShape's actual transform/alpha CALLS rather than
// real pixel output, same spirit as tests/preview.test.ts's fakeCtx().
function recordingCtx() {
  const calls: { name: string; args: unknown[] }[] = [];
  const alphas: number[] = [];
  const ctx: any = {
    get globalAlpha() {
      return this._alpha ?? 1;
    },
    set globalAlpha(v: number) {
      this._alpha = v;
      alphas.push(v);
    },
  };
  for (const name of [
    'save', 'restore', 'translate', 'rotate', 'scale', 'beginPath', 'moveTo', 'lineTo',
    'closePath', 'fill', 'stroke', 'ellipse', 'rect', 'bezierCurveTo', 'arc', 'fillRect', 'strokeRect',
  ]) {
    ctx[name] = (...args: unknown[]) => calls.push({ name, args });
  }
  ctx.createRadialGradient = () => ({ addColorStop: () => {} });
  ctx.createLinearGradient = () => ({ addColorStop: () => {} });
  ctx.measureText = () => ({ width: 5 });
  return { ctx, calls, alphas };
}

const baseRect: VectorShape = {
  id: 'r',
  kind: 'rect',
  x: 0,
  y: 0,
  w: 10,
  h: 10,
  rotation: 0,
  fill: { kind: 'flat', color: '#fff' },
  stroke: 'none',
  strokeWidth: 0,
};

describe('drawVectorShape — per-shape animation (animTracks/animStatics)', () => {
  it('a shape with no animTracks/animStatics renders exactly as before — no extra transform, full alpha', () => {
    const { ctx, calls, alphas } = recordingCtx();
    drawVectorShape(ctx, baseRect, 0, 0, 1, 0, 0, 0);
    expect(alphas[0]).toBe(1);
    expect(calls.filter((c) => c.name === 'translate').length).toBe(0);
    expect(calls.filter((c) => c.name === 'rotate').length).toBe(0);
  });

  it('an opacity track attenuates the layer alpha at the given time', () => {
    const animated: VectorShape = {
      ...baseRect,
      animTracks: { opacity: [{ id: 'k1', t: 0, v: 1, ease: 'linear' }, { id: 'k2', t: 10, v: 0, ease: 'linear' }] },
    };
    const { ctx, alphas } = recordingCtx();
    drawVectorShape(ctx, animated, 0, 0, 1, 0, 0, 5);
    expect(alphas[0]).toBeCloseTo(0.5, 5);
  });

  it('a rotation track applies an extra pivot transform around the shape\'s own center', () => {
    const animated: VectorShape = {
      ...baseRect,
      animTracks: { rotation: [{ id: 'k1', t: 0, v: 0, ease: 'linear' }, { id: 'k2', t: 10, v: Math.PI, ease: 'linear' }] },
    };
    const { ctx, calls } = recordingCtx();
    drawVectorShape(ctx, animated, 0, 0, 1, 0, 0, 5);
    const rotateCalls = calls.filter((c) => c.name === 'rotate');
    expect(rotateCalls.length).toBeGreaterThan(0);
    expect(rotateCalls[0].args[0]).toBeCloseTo(Math.PI / 2, 5);
  });

  it('static (non-keyframed) animStatics opacity of 0 hides the shape entirely, before any draw call', () => {
    const animated: VectorShape = { ...baseRect, animStatics: { opacity: 0 } };
    const { ctx, calls } = recordingCtx();
    drawVectorShape(ctx, animated, 0, 0, 1, 0, 0, 0);
    expect(calls.length).toBe(0);
  });

  it('a plain x/y nudge composes with the layer-level alpha rather than replacing it', () => {
    const animated: VectorShape = { ...baseRect, animStatics: { x: 5, y: -5 } };
    const { ctx, calls, alphas } = recordingCtx();
    drawVectorShape(ctx, animated, 0, 0, 0.5, 0, 0, 0);
    expect(alphas[0]).toBeCloseTo(0.5, 5);
    const translateCalls = calls.filter((c) => c.name === 'translate');
    expect(translateCalls.length).toBeGreaterThan(0);
  });
});
