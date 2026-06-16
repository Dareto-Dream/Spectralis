import { describe, expect, it } from 'vitest';
import { drawPreviewFrame, initialFrameState, aspectSize } from '../src/preview/drawPreview';
import type { Project } from '../src/types/project';

function fakeCtx() {
  const calls: string[] = [];
  return new Proxy(
    {},
    {
      get(_target, prop) {
        if (prop === 'createLinearGradient') {
          return () => ({ addColorStop: () => {} });
        }
        calls.push(String(prop));
        return () => {};
      },
    }
  ) as unknown as CanvasRenderingContext2D;
}

const project: Project = {
  formatVersion: 2,
  meta: { title: 'T', artist: '', slug: 't', songEnd: 60, aspect: '9x16' },
  sections: [{ id: 's1', label: 'S1', start: 0, end: 60, hue: 0, hue2: 0, intensity: 0.5, noLyrics: false }],
  layers: [],
};

describe('drawPreviewFrame — beat flash', () => {
  it('spikes beatFlash on a sharp peak rise above threshold', () => {
    const state = initialFrameState();
    drawPreviewFrame(fakeCtx(), 270, 480, project, 0, 0, { peak: 0.3, rms: 0.1 }, state);
    expect(state.beatFlash).toBeGreaterThan(0.8); // 1.0 * 0.9 decay applied once
  });

  it('does not spike on a small or sub-threshold peak', () => {
    const state = initialFrameState();
    drawPreviewFrame(fakeCtx(), 270, 480, project, 0, 0, { peak: 0.05, rms: 0.01 }, state);
    expect(state.beatFlash).toBeLessThan(0.1);
  });

  it('decays toward zero across repeated quiet frames', () => {
    const state = initialFrameState();
    drawPreviewFrame(fakeCtx(), 270, 480, project, 0, 0, { peak: 0.3, rms: 0.1 }, state);
    const afterSpike = state.beatFlash;
    for (let i = 0; i < 10; i++) {
      drawPreviewFrame(fakeCtx(), 270, 480, project, 0, 0, { peak: 0, rms: 0 }, state);
    }
    expect(state.beatFlash).toBeLessThan(afterSpike);
  });
});

describe('aspectSize', () => {
  it('maps each aspect to its known canvas dimensions', () => {
    expect(aspectSize('9x16')).toEqual({ w: 270, h: 480 });
    expect(aspectSize('16x9')).toEqual({ w: 480, h: 270 });
    expect(aspectSize('1x1')).toEqual({ w: 380, h: 380 });
  });
});
