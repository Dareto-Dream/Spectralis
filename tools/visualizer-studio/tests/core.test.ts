import { describe, expect, it } from 'vitest';
import { lerp, clamp01, lerpHue } from '../src/core/math.js';
import { EASE, evalTrack } from '../src/core/ease.js';
import { hash, seeded } from '../src/core/hash.js';
import { parseLRC, flattenWords } from '../src/core/lrc.js';

describe('math', () => {
  it('lerp interpolates linearly', () => {
    expect(lerp(0, 10, 0.5)).toBe(5);
  });
  it('clamp01 clamps to [0,1]', () => {
    expect(clamp01(-1)).toBe(0);
    expect(clamp01(2)).toBe(1);
  });
  it('lerpHue takes the shortest path across the wheel', () => {
    expect(lerpHue(350, 10, 0.5)).toBe(0);
  });
});

describe('evalTrack', () => {
  const track = [
    { id: 'a', t: 0, v: 0, ease: 'linear' },
    { id: 'b', t: 10, v: 100, ease: 'linear' },
  ];
  it('returns default for an empty track', () => {
    expect(evalTrack([], 5, -1)).toBe(-1);
  });
  it('returns the single value for a one-keyframe track', () => {
    expect(evalTrack([{ id: 'a', t: 0, v: 42, ease: 'linear' }], 5, -1)).toBe(42);
  });
  it('interpolates linearly between two keyframes', () => {
    expect(evalTrack(track, 5, -1)).toBe(50);
  });
  it('holds at the start value when ease is hold', () => {
    const holdTrack = [
      { id: 'a', t: 0, v: 0, ease: 'hold' },
      { id: 'b', t: 10, v: 100, ease: 'linear' },
    ];
    expect(evalTrack(holdTrack, 9, -1)).toBe(0);
  });
  it('clamps before the first and after the last keyframe', () => {
    expect(evalTrack(track, -5, -1)).toBe(0);
    expect(evalTrack(track, 50, -1)).toBe(100);
  });
});

describe('EASE table', () => {
  it('has all 7 named eases and each maps 0 to 0 and 1 to 1 (except hold)', () => {
    expect(Object.keys(EASE).sort()).toEqual(
      ['hold', 'incubic', 'inoutcubic', 'insine', 'linear', 'outcubic', 'outsine'].sort()
    );
    for (const name of ['linear', 'incubic', 'outcubic', 'inoutcubic', 'insine', 'outsine']) {
      expect(EASE[name](0)).toBeCloseTo(0);
      expect(EASE[name](1)).toBeCloseTo(1);
    }
  });
});

describe('hash/seeded', () => {
  it('is deterministic for the same input', () => {
    expect(hash('w0:hello')).toBe(hash('w0:hello'));
  });
  it('produces a repeatable PRNG sequence from the same seed', () => {
    const a = seeded(hash('seed'));
    const b = seeded(hash('seed'));
    expect(a()).toBe(b());
    expect(a()).toBe(b());
  });
});

describe('parseLRC', () => {
  it('parses plain [mm:ss.xx] lines with evenly distributed word timing', () => {
    const lines = parseLRC('[00:01.00]hello world\n[00:03.00]goodbye');
    expect(lines).toHaveLength(2);
    expect(lines[0].time).toBe(1);
    expect(lines[0].words.map((w) => w.text)).toEqual(['hello', 'world']);
  });
  it('parses word-level <mm:ss.xx> sub-tags when present', () => {
    const lines = parseLRC('[00:01.00]<00:01.00>hi <00:01.50>there');
    expect(lines[0].words.map((w) => w.time)).toEqual([1, 1.5]);
  });
  it('skips lines that do not match the timestamp format', () => {
    const lines = parseLRC('not a lyric line\n[00:01.00]real line');
    expect(lines).toHaveLength(1);
  });
});

describe('flattenWords', () => {
  it('marks key words from the key set (case/punctuation insensitive)', () => {
    const lines = parseLRC('[00:01.00]Hello, world!');
    const words = flattenWords(lines, { hello: true });
    expect(words[0].isKey).toBe(true);
    expect(words[1].isKey).toBe(false);
  });
});
