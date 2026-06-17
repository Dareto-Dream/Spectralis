import { EASE } from '../core/ease.js';
import type { Ease } from '../types/project';

export const EASE_NAMES: Ease[] = ['hold', 'linear', 'incubic', 'outcubic', 'inoutcubic', 'insine', 'outsine'];

export interface CurvePoint {
  x: number;
  y: number;
}

// Visualizes the 7 named eases (matches what the export format can actually
// represent) rather than authoring custom bezier handles — see plan's "Curve
// editor — scope decision". `hold` isn't a real function in EASE (evalTrack
// special-cases it to mean "flat until the segment ends, then jump"), so it
// gets its own step-shaped sample instead of calling EASE.hold (which just
// returns 0 for every t and would draw as a flat zero line).
export function sampleEase(name: Ease, steps = 24): CurvePoint[] {
  const fn = EASE[name] ?? EASE.linear;
  const points: CurvePoint[] = [];
  for (let i = 0; i <= steps; i++) {
    const t = i / steps;
    const y = name === 'hold' ? (i === steps ? 1 : 0) : fn(t);
    points.push({ x: t, y });
  }
  return points;
}
