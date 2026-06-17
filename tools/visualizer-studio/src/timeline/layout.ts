// SHARED layout math for the timeline canvas — draw.ts and interactions.ts both
// import exclusively from here. The old tool computed the same pixel bands twice
// (once in its draw code, once in its hit-testing) with duplicated literals; that's
// exactly the kind of drift this file exists to make structurally impossible.

export interface TimelineLayout {
  rulerH: number;
  waveH: number;
  secH: number;
  rowH: number;
  laneGap: number;
  laneH: number;
  rulerY: number;
  waveY: number;
  secY: number;
  overviewY: number;
  overviewH: number;
  laneY: number;
  totalH: number;
}

const RULER_H = 22;
const WAVE_H = 36;
const SEC_H = 20;
const ROW_H = 14;
const LANE_GAP = 2;
const LANE_H = 90;
const OVERVIEW_TO_LANE_GAP = 8;

export function computeLayout(layerCount: number): TimelineLayout {
  const rulerY = 0;
  const waveY = rulerY + RULER_H;
  const secY = waveY + WAVE_H;
  const overviewY = secY + SEC_H;
  const overviewH = layerCount > 0 ? layerCount * ROW_H + (layerCount - 1) * LANE_GAP : 0;
  const laneY = overviewY + overviewH + OVERVIEW_TO_LANE_GAP;
  const totalH = laneY + LANE_H;
  return {
    rulerH: RULER_H,
    waveH: WAVE_H,
    secH: SEC_H,
    rowH: ROW_H,
    laneGap: LANE_GAP,
    laneH: LANE_H,
    rulerY,
    waveY,
    secY,
    overviewY,
    overviewH,
    laneY,
    totalH,
  };
}

export function tX(t: number, pxPerSec: number): number {
  return t * pxPerSec;
}

export function xT(x: number, pxPerSec: number): number {
  return x / pxPerSec;
}

export type Band = 'ruler' | 'wave' | 'sections' | 'overview' | 'lane' | 'below';

export function bandAtY(layout: TimelineLayout, y: number): Band {
  if (y < layout.waveY) return 'ruler';
  if (y < layout.secY) return 'wave';
  if (y < layout.overviewY) return 'sections';
  if (y < layout.laneY) return 'overview';
  if (y < layout.totalH) return 'lane';
  return 'below';
}

// Returns the layer-list index for the compact overview row at `y`, or null if
// `y` falls in the gap between rows, above the overview band, or below it —
// callers still need to bounds-check the result against the actual layer count.
export function overviewRowIndex(layout: TimelineLayout, y: number): number | null {
  if (y < layout.overviewY || y >= layout.laneY) return null;
  const rel = y - layout.overviewY;
  const stride = layout.rowH + layout.laneGap;
  const idx = Math.floor(rel / stride);
  const rowStart = idx * stride;
  if (rel - rowStart >= layout.rowH) return null;
  return idx;
}
