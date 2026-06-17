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
