import { renderLayerAt, sectionAt, drawSectionWash } from '../core/render.js';
import type { Project } from '../types/project';

export interface PreviewFrameState {
  beatFlash: number;
  lastPeakSmooth: number;
}

export function initialFrameState(): PreviewFrameState {
  return { beatFlash: 0, lastPeakSmooth: 0 };
}

// Kept as a plain, DOM-adjacent-but-framework-free function (takes a 2D context
// directly) so it's testable without mounting a component and reusable outside
// Svelte's render cycle — PreviewCanvas.svelte just drives it from a RAF loop.
export function drawPreviewFrame(
  ctx: CanvasRenderingContext2D,
  W: number,
  H: number,
  project: Project,
  playhead: number,
  nowMs: number,
  level: { peak: number; rms: number },
  frameState: PreviewFrameState,
  soloedLayerIds?: ReadonlySet<string>,
  // Workspace canvas only (preview/WorkspaceCanvas.svelte) — leaves untouched
  // pixels transparent instead of opaque black, so a CSS checkerboard behind
  // the canvas shows through wherever nothing's actually drawn. The read-only
  // Preview viewport and export both keep the real opaque-black capsule look
  // (this defaults to false, i.e. their existing behavior, unchanged).
  opts?: {
    transparentBg?: boolean;
    // Workspace canvas only, while actively painting a bitmap layer with the
    // brush tool — omits that one layer from this pass so the live in-progress
    // paint buffer (drawn separately, once, at the right transform/opacity)
    // doesn't get composited a second time underneath it. See
    // WorkspaceCanvas.svelte's tick()/drawLiveBrushLayer.
    skipLayerId?: string | null;
  }
): void {
  if (opts?.transparentBg) {
    ctx.clearRect(0, 0, W, H);
  } else {
    ctx.fillStyle = '#000';
    ctx.fillRect(0, 0, W, H);
  }
  drawSectionWash(ctx, W, H, sectionAt(project.sections, playhead));

  const pkD = level.peak - frameState.lastPeakSmooth;
  if (pkD > 0.14 && level.peak > 0.24) frameState.beatFlash = 1.0;
  frameState.lastPeakSmooth = frameState.lastPeakSmooth * 0.83 + level.peak * 0.17;
  frameState.beatFlash *= 0.9;

  for (const layer of project.layers) {
    if (soloedLayerIds && soloedLayerIds.size > 0 && !soloedLayerIds.has(layer.id)) continue;
    if (opts?.skipLayerId && layer.id === opts.skipLayerId) continue;
    renderLayerAt(ctx, layer, playhead, W, H, nowMs, frameState.beatFlash);
  }

  if (frameState.beatFlash > 0.3) {
    ctx.fillStyle = `rgba(255,255,255,${(frameState.beatFlash - 0.3) * 0.06})`;
    ctx.fillRect(0, 0, W, H);
  }
}

// Bumped to a real Full-HD-class working resolution — every axis is scaled
// up by the exact same 4x factor from the old placeholder values (270/480/
// 380), so no authored shape/text size or the minSide/380 normalization in
// vectorHitTest.ts's resolveLayerTransform changes proportionally, only the
// actual pixel resolution layers get edited/painted/rasterized at (sharper
// paintbrush strokes and rasterized bitmaps, most visibly). 16:9 lands
// exactly on 1920x1080; 9:16 (the default) on its vertical mirror,
// 1080x1920; 1:1 on 1520x1520. The real exported/deployed visualizer already
// scales to whatever the actual playback device's resolution is (that's the
// whole point of the x/270,y/480 reference-space convention) — this only
// affects the Studio's own in-editor canvas.
export function aspectSize(aspect: Project['meta']['aspect']): { w: number; h: number } {
  if (aspect === '16x9') return { w: 1920, h: 1080 };
  if (aspect === '1x1') return { w: 1520, h: 1520 };
  return { w: 1080, h: 1920 };
}
