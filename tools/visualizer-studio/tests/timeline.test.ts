import { describe, expect, it } from 'vitest';
import { computeLayout } from '../src/timeline/layout';
import {
  hitTestActiveLaneKeyframe,
  hitTestOverviewRow,
  hitTestSection,
  hitTestSectionEdge,
  hitTestScrub,
  keyframesInMarquee,
} from '../src/timeline/interactions';
import { snap, snapTargets } from '../src/timeline/snapping';
import { copyKeyframes, pasteAtPlayhead, pasteAtOriginalTimes, hasClipboard, clearClipboard } from '../src/timeline/clipboard';
import { sampleEase, EASE_NAMES } from '../src/timeline/curveEditor';
import { drawTimeline } from '../src/timeline/draw';
import type { Project } from '../src/types/project';

const pxPerSec = 80;

function fakeCtx() {
  return new Proxy(
    {},
    {
      get(_t, prop) {
        if (prop === 'createLinearGradient') return () => ({ addColorStop: () => {} });
        return () => {};
      },
    }
  ) as unknown as CanvasRenderingContext2D;
}

function project(): Project {
  return {
    formatVersion: 2,
    meta: { title: 'T', artist: '', slug: 't', songEnd: 60, aspect: '9x16' },
    sections: [
      { id: 'a', label: 'A', start: 0, end: 20, hue: 0, hue2: 0, intensity: 0.5, noLyrics: false },
      { id: 'b', label: 'B', start: 20, end: 60, hue: 0, hue2: 0, intensity: 0.5, noLyrics: false },
    ],
    layers: [
      {
        id: 'l1',
        name: 'Orb',
        type: 'orb',
        visible: true,
        statics: { x: 135, y: 220, scale: 1, rotation: 0, opacity: 1, hueA: 220, hueB: 260 },
        tracks: {
          x: [],
          y: [],
          scale: [
            { id: 'k1', t: 5, v: 0, ease: 'linear' },
            { id: 'k2', t: 15, v: 1, ease: 'linear' },
          ],
          rotation: [],
          opacity: [],
          hueA: [],
          hueB: [],
        },
        params: { radius: 60 },
      },
    ],
  };
}

describe('hitTestActiveLaneKeyframe', () => {
  it('hits a keyframe within tolerance in the lane band', () => {
    const p = project();
    const layout = computeLayout(1);
    const midY = layout.laneY + layout.laneH / 2;
    const x = 5 * pxPerSec; // k1's time
    const hit = hitTestActiveLaneKeyframe(layout, p, 'l1', 'scale', pxPerSec, x, midY);
    expect(hit?.keyframeId).toBe('k1');
  });

  it('misses outside the tolerance', () => {
    const p = project();
    const layout = computeLayout(1);
    const midY = layout.laneY + layout.laneH / 2;
    const x = 5 * pxPerSec + 50;
    expect(hitTestActiveLaneKeyframe(layout, p, 'l1', 'scale', pxPerSec, x, midY)).toBeNull();
  });

  it('returns null outside the lane band or with no active track', () => {
    const p = project();
    const layout = computeLayout(1);
    expect(hitTestActiveLaneKeyframe(layout, p, 'l1', 'scale', pxPerSec, 5 * pxPerSec, 0)).toBeNull();
    expect(hitTestActiveLaneKeyframe(layout, p, 'l1', null, pxPerSec, 5 * pxPerSec, layout.laneY + layout.laneH / 2)).toBeNull();
  });
});

describe('hitTestOverviewRow', () => {
  it('resolves a y in the overview band to the correct layer', () => {
    const p = project();
    const layout = computeLayout(1);
    expect(hitTestOverviewRow(layout, p, layout.overviewY)?.id).toBe('l1');
  });
});

describe('hitTestSection / hitTestSectionEdge', () => {
  it('finds the section containing a given x', () => {
    const p = project();
    const layout = computeLayout(1);
    expect(hitTestSection(layout, p, pxPerSec, 5 * pxPerSec, layout.secY)).toBe('a');
    expect(hitTestSection(layout, p, pxPerSec, 25 * pxPerSec, layout.secY)).toBe('b');
  });

  it('finds a section edge within tolerance', () => {
    const p = project();
    const layout = computeLayout(1);
    const edge = hitTestSectionEdge(layout, p, pxPerSec, 20 * pxPerSec, layout.secY);
    // x=20*pxPerSec is exactly section a's end AND section b's start — a's end wins (checked first)
    expect(edge).toEqual({ sectionId: 'a', edge: 'end' });
  });
});

describe('hitTestScrub', () => {
  it('is true over the ruler and waveform bands, false elsewhere', () => {
    const layout = computeLayout(1);
    expect(hitTestScrub(layout, layout.rulerY)).toBe(true);
    expect(hitTestScrub(layout, layout.waveY)).toBe(true);
    expect(hitTestScrub(layout, layout.secY)).toBe(false);
    expect(hitTestScrub(layout, layout.laneY)).toBe(false);
  });
});

describe('keyframesInMarquee', () => {
  it('selects keyframes within the rect on the active track only', () => {
    const p = project();
    const ids = keyframesInMarquee(p, 'l1', 'scale', pxPerSec, { x0: 0, y0: 0, x1: 10 * pxPerSec, y1: 999 });
    expect(ids).toEqual(['k1']);
  });

  it('returns nothing with no active layer/track', () => {
    const p = project();
    expect(keyframesInMarquee(p, null, null, pxPerSec, { x0: 0, y0: 0, x1: 999, y1: 999 })).toEqual([]);
  });
});

describe('snap', () => {
  it('snaps to the nearest target within tolerance', () => {
    expect(snap(10.05, [10], pxPerSec, 6)).toBe(10);
  });

  it('leaves t unchanged when nothing is within tolerance', () => {
    expect(snap(10.5, [10], pxPerSec, 6)).toBe(10.5);
  });

  it('snapTargets includes the playhead, section bounds, and other keyframes', () => {
    const p = project();
    const targets = snapTargets(p, 30);
    expect(targets).toContain(30); // playhead
    expect(targets).toContain(0);
    expect(targets).toContain(20);
    expect(targets).toContain(60);
    expect(targets).toContain(5);
    expect(targets).toContain(15);
  });

  it('snapTargets excludes the keyframe(s) being dragged', () => {
    const p = project();
    const targets = snapTargets(p, 30, new Set(['k1']));
    expect(targets).not.toContain(5);
    expect(targets).toContain(15);
  });
});

describe('clipboard', () => {
  it('paste-at-playhead uses the relative offset from the copy anchor', () => {
    clearClipboard();
    copyKeyframes([{ layerId: 'l1', trackKey: 'scale', t: 10, v: 5, ease: 'linear' }], 8);
    const pasted = pasteAtPlayhead(20);
    expect(pasted[0].t).toBe(22); // relT was +2, pasted at 20+2
  });

  it('paste-at-original-times ignores the new anchor entirely', () => {
    clearClipboard();
    copyKeyframes([{ layerId: 'l1', trackKey: 'scale', t: 10, v: 5, ease: 'linear' }], 8);
    const pasted = pasteAtOriginalTimes();
    expect(pasted[0].t).toBe(10);
  });

  it('hasClipboard reflects copy/clear state', () => {
    clearClipboard();
    expect(hasClipboard()).toBe(false);
    copyKeyframes([{ layerId: 'l1', trackKey: 'scale', t: 0, v: 0, ease: 'linear' }], 0);
    expect(hasClipboard()).toBe(true);
    clearClipboard();
    expect(hasClipboard()).toBe(false);
  });
});

describe('sampleEase', () => {
  it('every real ease starts at 0 and ends at 1', () => {
    for (const name of EASE_NAMES.filter((n) => n !== 'hold')) {
      const pts = sampleEase(name, 10);
      expect(pts[0].y).toBeCloseTo(0);
      expect(pts[pts.length - 1].y).toBeCloseTo(1);
    }
  });

  it('hold renders as a flat-then-jump step, not EASE.hold\'s literal always-0', () => {
    const pts = sampleEase('hold', 10);
    expect(pts[0].y).toBe(0);
    expect(pts[5].y).toBe(0);
    expect(pts[pts.length - 1].y).toBe(1);
  });
});

describe('drawTimeline — smoke test', () => {
  it('runs without throwing across selection/waveform states', () => {
    const p = project();
    const layout = computeLayout(p.layers.length);
    const base = { ctx: fakeCtx(), W: 800, H: layout.totalH, layout, project: p, playhead: 10, pxPerSec, scrollX: 0, waveformPeaks: null };
    expect(() =>
      drawTimeline({ ...base, selection: { layerId: null, trackKey: null, keyframeIds: new Set(), sectionId: null } })
    ).not.toThrow();
    expect(() =>
      drawTimeline({
        ...base,
        selection: { layerId: 'l1', trackKey: 'scale', keyframeIds: new Set(['k1']), sectionId: 'a' },
        waveformPeaks: new Float32Array([0.1, 0.5, 0.9]),
      })
    ).not.toThrow();
  });
});
