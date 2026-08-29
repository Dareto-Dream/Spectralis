import { describe, expect, it, beforeEach } from 'vitest';
import { workspaceView } from '../src/state/workspaceView.svelte';

describe('workspaceView.svelte.ts', () => {
  beforeEach(() => workspaceView.reset());

  it('defaults to 100% zoom, centered, unrotated', () => {
    expect(workspaceView).toMatchObject({ zoom: 1, panX: 0, panY: 0, rotation: 0 });
  });

  it('zoomStep(1) zooms in, zoomStep(-1) zooms back out to the same value', () => {
    const start = workspaceView.zoom;
    workspaceView.zoomStep(1);
    expect(workspaceView.zoom).toBeGreaterThan(start);
    workspaceView.zoomStep(-1);
    expect(workspaceView.zoom).toBeCloseTo(start, 5);
  });

  it('zoom is clamped to [0.1, 8] no matter how far you push it', () => {
    for (let i = 0; i < 100; i++) workspaceView.zoomStep(1);
    expect(workspaceView.zoom).toBeLessThanOrEqual(8);
    workspaceView.reset();
    for (let i = 0; i < 100; i++) workspaceView.zoomStep(-1);
    expect(workspaceView.zoom).toBeGreaterThanOrEqual(0.1);
  });

  it('setZoomClamped clamps direct assignment too (e.g. from a future zoom-% input)', () => {
    workspaceView.setZoomClamped(999);
    expect(workspaceView.zoom).toBe(8);
    workspaceView.setZoomClamped(-5);
    expect(workspaceView.zoom).toBe(0.1);
  });

  it('reset() clears pan/rotation/zoom back to identity', () => {
    workspaceView.panX = 100;
    workspaceView.panY = -50;
    workspaceView.rotation = 1.2;
    workspaceView.zoom = 2.5;
    workspaceView.reset();
    expect(workspaceView).toMatchObject({ zoom: 1, panX: 0, panY: 0, rotation: 0 });
  });
});
