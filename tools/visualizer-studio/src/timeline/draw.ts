import type { TimelineLayout } from './layout';
import { tX } from './layout';
import type { AnimKey, Project } from '../types/project';

const COLORS = {
  bg3: '#1e1e27',
  bg4: '#26262f',
  line2: '#3a3a47',
  text: '#e7e7ee',
  dim: '#9494a6',
  dim2: '#6a6a7a',
  accent: '#7fb7ff',
  accent2: '#ffd06e',
  danger: '#ff6e6e',
};

export interface DrawSelection {
  layerId: string | null;
  trackKey: AnimKey | null;
  keyframeIds: ReadonlySet<string>;
  sectionId: string | null;
}

export interface DrawTimelineParams {
  ctx: CanvasRenderingContext2D;
  W: number;
  H: number;
  layout: TimelineLayout;
  project: Project;
  playhead: number;
  pxPerSec: number;
  scrollX: number;
  selection: DrawSelection;
  waveformPeaks: Float32Array | null;
}

export function drawTimeline(p: DrawTimelineParams): void {
  const { ctx, W, H } = p;
  ctx.save();
  ctx.fillStyle = COLORS.bg3;
  ctx.fillRect(0, 0, W, H);
  ctx.translate(-p.scrollX, 0);

  drawRuler(p);
  drawWaveform(p);
  drawSections(p);
  drawOverview(p);
  drawActiveLane(p);
  drawPlayhead(p);

  ctx.restore();
}

function xOf(p: DrawTimelineParams, t: number): number {
  return tX(t, p.pxPerSec);
}

function drawRuler(p: DrawTimelineParams) {
  const { ctx, layout, project, pxPerSec } = p;
  ctx.save();
  ctx.fillStyle = COLORS.bg4;
  ctx.fillRect(0, layout.rulerY, xOf(p, project.meta.songEnd) + 40, layout.rulerH);
  ctx.strokeStyle = COLORS.line2;
  ctx.fillStyle = COLORS.dim;
  ctx.font = '10px sans-serif';
  ctx.textBaseline = 'middle';
  const step = pxPerSec >= 60 ? 1 : pxPerSec >= 20 ? 5 : 10;
  for (let t = 0; t <= project.meta.songEnd + step; t += step) {
    const x = xOf(p, t);
    ctx.beginPath();
    ctx.moveTo(x, layout.rulerY);
    ctx.lineTo(x, layout.rulerY + layout.rulerH);
    ctx.stroke();
    ctx.fillText(String(t), x + 3, layout.rulerY + layout.rulerH / 2);
  }
  ctx.restore();
}

function drawWaveform(p: DrawTimelineParams) {
  const { ctx, layout, project, waveformPeaks } = p;
  const w = xOf(p, project.meta.songEnd);
  ctx.save();
  ctx.fillStyle = COLORS.bg3;
  ctx.fillRect(0, layout.waveY, w, layout.waveH);
  if (!waveformPeaks) {
    ctx.fillStyle = COLORS.dim2;
    ctx.font = '11px sans-serif';
    ctx.textBaseline = 'middle';
    ctx.fillText('No audio loaded — waveform will appear here', 8, layout.waveY + layout.waveH / 2);
  } else {
    ctx.fillStyle = COLORS.accent;
    const mid = layout.waveY + layout.waveH / 2;
    const n = waveformPeaks.length;
    for (let i = 0; i < n; i++) {
      const t = (i / n) * project.meta.songEnd;
      const x = xOf(p, t);
      if (x < 0 || x > w) continue;
      const h = waveformPeaks[i] * (layout.waveH / 2);
      ctx.fillRect(x, mid - h, 1, h * 2);
    }
  }
  ctx.restore();
}

function drawSections(p: DrawTimelineParams) {
  const { ctx, layout, project, selection } = p;
  ctx.save();
  for (const sec of project.sections) {
    const x0 = xOf(p, sec.start);
    const x1 = xOf(p, sec.end);
    const g = ctx.createLinearGradient(x0, 0, x1, 0);
    g.addColorStop(0, `hsl(${sec.hue},40%,22%)`);
    g.addColorStop(1, `hsl(${sec.hue2},40%,18%)`);
    ctx.fillStyle = g;
    ctx.fillRect(x0, layout.secY, x1 - x0, layout.secH);
    ctx.strokeStyle = sec.id === selection.sectionId ? COLORS.accent2 : 'rgba(255,255,255,.25)';
    ctx.lineWidth = sec.id === selection.sectionId ? 2 : 1;
    ctx.strokeRect(x0, layout.secY, x1 - x0, layout.secH);
    ctx.fillStyle = COLORS.text;
    ctx.font = '10px sans-serif';
    ctx.textBaseline = 'middle';
    ctx.fillText(sec.label + (sec.noLyrics ? ' 🔇' : ''), x0 + 4, layout.secY + layout.secH / 2);
  }
  ctx.restore();
}

function drawOverview(p: DrawTimelineParams) {
  const { ctx, layout, project, selection } = p;
  ctx.save();
  project.layers.forEach((layer, i) => {
    const y = layout.overviewY + i * (layout.rowH + layout.laneGap);
    if (layer.id === selection.layerId) {
      ctx.fillStyle = COLORS.bg4;
      ctx.fillRect(0, y, xOf(p, project.meta.songEnd), layout.rowH);
    }
    ctx.fillStyle = layer.visible ? COLORS.dim : COLORS.dim2;
    ctx.font = '9px sans-serif';
    ctx.textBaseline = 'middle';
    ctx.fillText(layer.name, 4, y + layout.rowH / 2);
    ctx.fillStyle = COLORS.accent;
    for (const key of Object.keys(layer.tracks) as AnimKey[]) {
      for (const kf of layer.tracks[key]) {
        const x = xOf(p, kf.t);
        ctx.fillRect(x - 1, y + layout.rowH / 2 - 1, 2, 2);
      }
    }
  });
  ctx.restore();
}

function drawActiveLane(p: DrawTimelineParams) {
  const { ctx, layout, selection, project } = p;
  ctx.save();
  ctx.fillStyle = COLORS.bg3;
  ctx.fillRect(0, layout.laneY, xOf(p, project.meta.songEnd), layout.laneH);
  const layer = project.layers.find((l) => l.id === selection.layerId);
  if (!layer || !selection.trackKey) {
    ctx.fillStyle = COLORS.dim2;
    ctx.font = '11px sans-serif';
    ctx.textBaseline = 'middle';
    ctx.fillText('Select a keyframed property to edit its curve here', 8, layout.laneY + layout.laneH / 2);
    ctx.restore();
    return;
  }
  const track = layer.tracks[selection.trackKey];
  const midY = layout.laneY + layout.laneH / 2;
  ctx.strokeStyle = COLORS.line2;
  ctx.beginPath();
  track.forEach((kf, i) => {
    const x = xOf(p, kf.t);
    if (i === 0) ctx.moveTo(x, midY);
    else ctx.lineTo(x, midY);
  });
  ctx.stroke();
  for (const kf of track) {
    const x = xOf(p, kf.t);
    const selected = selection.keyframeIds.has(kf.id);
    ctx.fillStyle = selected ? COLORS.accent2 : COLORS.accent;
    drawDiamond(ctx, x, midY, 5);
  }
  ctx.restore();
}

function drawDiamond(ctx: CanvasRenderingContext2D, cx: number, cy: number, r: number) {
  ctx.beginPath();
  ctx.moveTo(cx, cy - r);
  ctx.lineTo(cx + r, cy);
  ctx.lineTo(cx, cy + r);
  ctx.lineTo(cx - r, cy);
  ctx.closePath();
  ctx.fill();
}

function drawPlayhead(p: DrawTimelineParams) {
  const { ctx, layout, playhead } = p;
  const x = xOf(p, playhead);
  ctx.save();
  ctx.strokeStyle = COLORS.danger;
  ctx.lineWidth = 1.5;
  ctx.beginPath();
  ctx.moveTo(x, layout.rulerY);
  ctx.lineTo(x, layout.totalH);
  ctx.stroke();
  ctx.restore();
}
