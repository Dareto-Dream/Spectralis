import type { TimelineLayout } from './layout';
import { xT, bandAtY, overviewRowIndex } from './layout';
import type { AnimKey, AnyLayer, Project } from '../types/project';

const KEYFRAME_HIT_TOLERANCE_PX = 7;
const SECTION_EDGE_TOLERANCE_PX = 5;

export interface HitKeyframe {
  layerId: string;
  trackKey: AnimKey;
  keyframeId: string;
}

// Checks the active-track lane first (larger target, matches the plan's stated
// hit-test priority) — callers that also want overview-row hits call
// hitTestOverviewRow separately once this returns null.
export function hitTestActiveLaneKeyframe(
  layout: TimelineLayout,
  project: Project,
  activeLayerId: string | null,
  activeTrackKey: AnimKey | null,
  pxPerSec: number,
  x: number,
  y: number
): HitKeyframe | null {
  if (bandAtY(layout, y) !== 'lane') return null;
  if (!activeLayerId || !activeTrackKey) return null;
  const layer = project.layers.find((l) => l.id === activeLayerId);
  if (!layer) return null;
  const midY = layout.laneY + layout.laneH / 2;
  if (Math.abs(y - midY) > 10) return null;
  const t = xT(x, pxPerSec);
  for (const kf of layer.tracks[activeTrackKey]) {
    if (Math.abs(kf.t - t) * pxPerSec <= KEYFRAME_HIT_TOLERANCE_PX) {
      return { layerId: layer.id, trackKey: activeTrackKey, keyframeId: kf.id };
    }
  }
  return null;
}

export function hitTestOverviewRow(layout: TimelineLayout, project: Project, y: number): AnyLayer | null {
  const idx = overviewRowIndex(layout, y);
  if (idx === null) return null;
  return project.layers[idx] ?? null;
}

export function hitTestSection(layout: TimelineLayout, project: Project, pxPerSec: number, x: number, y: number): string | null {
  if (bandAtY(layout, y) !== 'sections') return null;
  const t = xT(x, pxPerSec);
  const sec = project.sections.find((s) => t >= s.start && t <= s.end);
  return sec?.id ?? null;
}

export type SectionEdge = 'start' | 'end';
export interface HitSectionEdge {
  sectionId: string;
  edge: SectionEdge;
}

// Drag a section's edge directly on the timeline instead of only through its
// popover's number fields.
export function hitTestSectionEdge(
  layout: TimelineLayout,
  project: Project,
  pxPerSec: number,
  x: number,
  y: number
): HitSectionEdge | null {
  if (bandAtY(layout, y) !== 'sections') return null;
  for (const sec of project.sections) {
    const startX = sec.start * pxPerSec;
    const endX = sec.end * pxPerSec;
    if (Math.abs(x - startX) <= SECTION_EDGE_TOLERANCE_PX) return { sectionId: sec.id, edge: 'start' };
    if (Math.abs(x - endX) <= SECTION_EDGE_TOLERANCE_PX) return { sectionId: sec.id, edge: 'end' };
  }
  return null;
}

export function hitTestScrub(layout: TimelineLayout, y: number): boolean {
  const band = bandAtY(layout, y);
  return band === 'ruler' || band === 'wave';
}

// Region-aware cursor feedback (plan QoL §H): grab over a draggable keyframe,
// ew-resize over a section edge or the scrub band, grabbing while something is
// actively being dragged, default/pointer elsewhere. Kept a pure function of
// hit-test state so TimelineCanvas.svelte can call it from both pointermove
// (hover) and while `drag` is active.
export type TimelineCursor = 'default' | 'grab' | 'grabbing' | 'ew-resize' | 'pointer';

export function cursorAt(
  layout: TimelineLayout,
  project: Project,
  activeLayerId: string | null,
  activeTrackKey: AnimKey | null,
  pxPerSec: number,
  x: number,
  y: number,
  draggingKind: 'keyframe' | 'sectionEdge' | 'scrub' | 'marquee' | null
): TimelineCursor {
  if (draggingKind === 'keyframe') return 'grabbing';
  if (draggingKind === 'sectionEdge') return 'ew-resize';
  if (draggingKind === 'scrub') return 'ew-resize';
  if (draggingKind === 'marquee') return 'default';

  if (hitTestSectionEdge(layout, project, pxPerSec, x, y)) return 'ew-resize';
  if (hitTestActiveLaneKeyframe(layout, project, activeLayerId, activeTrackKey, pxPerSec, x, y)) return 'grab';
  if (hitTestScrub(layout, y)) return 'ew-resize';
  if (bandAtY(layout, y) === 'overview' || bandAtY(layout, y) === 'sections') return 'pointer';
  return 'default';
}

export interface MarqueeRect {
  x0: number;
  y0: number;
  x1: number;
  y1: number;
}

// Scoped to the active layer/track's lane (the common case) rather than every
// track across every layer — matches "checks the active-track lane first".
export function keyframesInMarquee(
  project: Project,
  activeLayerId: string | null,
  activeTrackKey: AnimKey | null,
  pxPerSec: number,
  rect: MarqueeRect
): string[] {
  if (!activeLayerId || !activeTrackKey) return [];
  const layer = project.layers.find((l) => l.id === activeLayerId);
  if (!layer) return [];
  const xMin = Math.min(rect.x0, rect.x1);
  const xMax = Math.max(rect.x0, rect.x1);
  return layer.tracks[activeTrackKey]
    .filter((kf) => {
      const x = kf.t * pxPerSec;
      return x >= xMin && x <= xMax;
    })
    .map((kf) => kf.id);
}
