// Spatial snap targets for the Inspector's x/y scrub fields — canvas center/
// edges/thirds plus every other layer's current x or y. Layer x/y are always
// authored in the fixed 270×480 design space core/render.js scales from at
// render time (`x * (W/aspectSize.w)`, independent of the project's actual
// aspect), so targets live in that same fixed space regardless of
// `meta.aspect` — no need to call aspectSize() here at all.
import { evalTrack } from '../core/ease.js';
import type { Project } from '../types/project';

const DESIGN_W = 270;
const DESIGN_H = 480;

export function spatialSnapTargets(project: Project, axis: 'x' | 'y', excludeLayerId: string | null, playhead: number): number[] {
  const size = axis === 'x' ? DESIGN_W : DESIGN_H;
  const targets = [0, size / 2, size, size / 3, (size * 2) / 3];
  for (const layer of project.layers) {
    if (layer.id === excludeLayerId) continue;
    const track = layer.tracks[axis];
    targets.push(track.length ? evalTrack(track, playhead, layer.statics[axis]) : layer.statics[axis]);
  }
  return targets;
}
