import { describe, expect, it } from 'vitest';
import { computeLayout, tX, xT, bandAtY, overviewRowIndex } from '../src/timeline/layout';

describe('computeLayout', () => {
  it('stacks bands in order: ruler, wave, sections, overview, lane', () => {
    const l = computeLayout(3);
    expect(l.rulerY).toBe(0);
    expect(l.waveY).toBe(l.rulerY + l.rulerH);
    expect(l.secY).toBe(l.waveY + l.waveH);
    expect(l.overviewY).toBe(l.secY + l.secH);
    expect(l.laneY).toBeGreaterThan(l.overviewY + l.overviewH);
    expect(l.totalH).toBe(l.laneY + l.laneH);
  });

  it('overview band height scales with layer count', () => {
    expect(computeLayout(0).overviewH).toBe(0);
    const one = computeLayout(1);
    const three = computeLayout(3);
    expect(one.overviewH).toBe(one.rowH);
    expect(three.overviewH).toBe(3 * three.rowH + 2 * three.laneGap);
    expect(three.totalH).toBeGreaterThan(one.totalH);
  });
});

describe('tX / xT', () => {
  it('round-trip through px-per-second', () => {
    expect(tX(10, 80)).toBe(800);
    expect(xT(800, 80)).toBe(10);
    expect(xT(tX(23.5, 42), 42)).toBeCloseTo(23.5);
  });
});

describe('bandAtY', () => {
  const l = computeLayout(2);

  it('classifies a y in each band correctly', () => {
    expect(bandAtY(l, 0)).toBe('ruler');
    expect(bandAtY(l, l.waveY)).toBe('wave');
    expect(bandAtY(l, l.secY)).toBe('sections');
    expect(bandAtY(l, l.overviewY)).toBe('overview');
    expect(bandAtY(l, l.laneY)).toBe('lane');
    expect(bandAtY(l, l.totalH)).toBe('below');
    expect(bandAtY(l, l.totalH + 500)).toBe('below');
  });
});

describe('overviewRowIndex', () => {
  it('resolves y to the correct row index within the overview band', () => {
    const l = computeLayout(4);
    expect(overviewRowIndex(l, l.overviewY)).toBe(0);
    expect(overviewRowIndex(l, l.overviewY + l.rowH + l.laneGap)).toBe(1);
    expect(overviewRowIndex(l, l.overviewY + 2 * (l.rowH + l.laneGap))).toBe(2);
  });

  it('returns null for y in the gap between rows', () => {
    const l = computeLayout(4);
    const gapY = l.overviewY + l.rowH; // first pixel of the gap after row 0
    expect(overviewRowIndex(l, gapY)).toBeNull();
  });

  it('returns null outside the overview band entirely', () => {
    const l = computeLayout(4);
    expect(overviewRowIndex(l, l.rulerY)).toBeNull();
    expect(overviewRowIndex(l, l.laneY)).toBeNull();
    expect(overviewRowIndex(l, l.totalH + 10)).toBeNull();
  });
});
