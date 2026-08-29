import { describe, expect, it } from 'vitest';
import {
  vectorizeOrb,
  vectorizeRing,
  vectorizeStreak,
  vectorizeWheel,
  vectorizeShard,
  vectorizeAmbientBeam,
  vectorizeText,
  bakeContinuousSpin,
} from '../src/lib/vectorize';

describe('vectorize.ts — legacy shape kinds to VectorShape[]', () => {
  it('vectorizeOrb produces one ellipse with a radial gradient fill', () => {
    const shapes = vectorizeOrb({ radius: 60 });
    expect(shapes).toHaveLength(1);
    expect(shapes[0].kind).toBe('ellipse');
    expect(shapes[0].fill.kind).toBe('radial');
  });

  it('vectorizeRing produces a stroked, unfilled ellipse', () => {
    const [ring] = vectorizeRing({ radius: 80, lineWidth: 2 });
    expect(ring.kind).toBe('ellipse');
    expect(ring.fill).toEqual({ kind: 'flat', color: 'none' });
    expect(ring.strokeWidth).toBe(2);
  });

  it('vectorizeStreak produces a rect centered on the origin', () => {
    const [streak] = vectorizeStreak({ length: 300, thickness: 14, mono: false });
    expect(streak.kind).toBe('rect');
    if (streak.kind === 'rect') {
      expect(streak.x).toBe(-150);
      expect(streak.w).toBe(300);
    }
  });

  it('vectorizeWheel produces one line per spoke plus a ring outline', () => {
    const shapes = vectorizeWheel({ radius: 90, spokes: 14, accentIdx: 0 });
    expect(shapes.filter((s) => s.kind === 'line')).toHaveLength(14);
    expect(shapes.filter((s) => s.kind === 'ellipse')).toHaveLength(1);
  });

  it('vectorizeShard produces a 5-sided polygon', () => {
    const [shard] = vectorizeShard({ size: 10 });
    expect(shard.kind).toBe('polygon');
    if (shard.kind === 'polygon') expect(shard.sides).toBe(5);
  });

  it('vectorizeAmbientBeam produces one wide gradient rect', () => {
    const shapes = vectorizeAmbientBeam({ bandHeight: 120 });
    expect(shapes).toHaveLength(1);
    expect(shapes[0].kind).toBe('rect');
  });

  it('vectorizeText produces one text shape carrying the source text/fontSize/tracking', () => {
    const [text] = vectorizeText({ text: 'HELLO', fontSize: 48, tracking: 2 });
    expect(text.kind).toBe('text');
    if (text.kind === 'text') {
      expect(text.text).toBe('HELLO');
      expect(text.fontSize).toBe(48);
    }
  });

  it('bakeContinuousSpin sweeps from 0 to spinRate*songEnd over [0, songEnd]', () => {
    const track = bakeContinuousSpin(0.5, 60);
    expect(track).toHaveLength(2);
    expect(track[0]).toMatchObject({ t: 0, v: 0 });
    expect(track[1]).toMatchObject({ t: 60, v: 30 });
  });
});
