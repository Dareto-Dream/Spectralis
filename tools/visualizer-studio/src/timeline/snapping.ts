import type { AnimKey, Project } from '../types/project';

// targets = playhead + every section start/end + every OTHER keyframe's time —
// picks the nearest one within `tolerancePx` (converted to seconds via pxPerSec),
// or returns `t` unchanged if nothing is close enough.
export function snap(t: number, targets: number[], pxPerSec: number, tolerancePx = 6): number {
  const tolSec = tolerancePx / pxPerSec;
  let best = t;
  let bestDist = tolSec;
  for (const target of targets) {
    const d = Math.abs(target - t);
    if (d <= bestDist) {
      bestDist = d;
      best = target;
    }
  }
  return best;
}

// Spatial counterpart to `snap()` above — same "nearest target within
// tolerance, else unchanged" shape, just directly in value-space instead of
// pixels-per-second. Used by scrubbableNumber.ts while dragging a layer's x/y
// field: "hold"-ease keyframes already give a snap-in-TIME for free, this is
// the snap-in-SPACE equivalent, reusing the same nearest-target math rather
// than duplicating it.
export function snapValue(v: number, targets: number[], tolerance = 4): number {
  let best = v;
  let bestDist = tolerance;
  for (const target of targets) {
    const d = Math.abs(target - v);
    if (d <= bestDist) {
      bestDist = d;
      best = target;
    }
  }
  return best;
}

export function snapTargets(project: Project, playhead: number, excludeKeyframeIds: ReadonlySet<string> = new Set()): number[] {
  const targets: number[] = [playhead];
  for (const sec of project.sections) targets.push(sec.start, sec.end);
  for (const layer of project.layers) {
    for (const key of Object.keys(layer.tracks) as AnimKey[]) {
      for (const kf of layer.tracks[key]) {
        if (!excludeKeyframeIds.has(kf.id)) targets.push(kf.t);
      }
    }
  }
  return targets;
}
