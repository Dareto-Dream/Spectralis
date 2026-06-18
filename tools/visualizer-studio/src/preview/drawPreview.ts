import { renderLayerAt, sectionAt, drawSectionWash } from '../core/render.js';
import type { AnyLayer, Project } from '../types/project';

export interface PreviewFrameState {
  beatFlash: number;
  lastPeakSmooth: number;
}

export function initialFrameState(): PreviewFrameState {
  return { beatFlash: 0, lastPeakSmooth: 0 };
}

function getLyricWords(layer: AnyLayer) {
  return layer.type === 'lyrics' ? layer.params.words : [];
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
  soloedLayerIds?: ReadonlySet<string>
): void {
  ctx.fillStyle = '#000';
  ctx.fillRect(0, 0, W, H);
  drawSectionWash(ctx, W, H, sectionAt(project.sections, playhead));

  const pkD = level.peak - frameState.lastPeakSmooth;
  if (pkD > 0.14 && level.peak > 0.24) frameState.beatFlash = 1.0;
  frameState.lastPeakSmooth = frameState.lastPeakSmooth * 0.83 + level.peak * 0.17;
  frameState.beatFlash *= 0.9;

  for (const layer of project.layers) {
    if (soloedLayerIds && soloedLayerIds.size > 0 && !soloedLayerIds.has(layer.id)) continue;
    renderLayerAt(ctx, layer, playhead, W, H, nowMs, frameState.beatFlash, getLyricWords, project.sections);
  }

  if (frameState.beatFlash > 0.3) {
    ctx.fillStyle = `rgba(255,255,255,${(frameState.beatFlash - 0.3) * 0.06})`;
    ctx.fillRect(0, 0, W, H);
  }
}

export function aspectSize(aspect: Project['meta']['aspect']): { w: number; h: number } {
  if (aspect === '16x9') return { w: 480, h: 270 };
  if (aspect === '1x1') return { w: 380, h: 380 };
  return { w: 270, h: 480 };
}
