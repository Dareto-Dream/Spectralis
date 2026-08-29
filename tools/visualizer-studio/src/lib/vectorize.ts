// Converts each of the 6 legacy procedural "shape" layer kinds (orb/ring/
// streak/wheel/ambientBeam/shard — removed from LayerType, see types/
// project.ts) into an equivalent VectorShape[]. Used by both the v3 project
// migration (lib/migrate.ts, for old saves) and the rebuilt lib/templates.ts
// (for fresh starter projects) — one implementation, so migrated saves and
// new templates never drift apart.
//
// These build DATA (shapes to store), not canvas draw calls — the geometry/
// gradient choices below are a faithful visual approximation of the old
// core/shapes.js draw functions (now removed, since no runtime layer type
// reaches them anymore), not a pixel-identical port. In particular the old
// orb's 3-stop gradient (distinct lightness/alpha per stop) collapses to a
// simpler 2-hue radial here — real glow comes from the `glow` field, not
// gradient trickery.
import type { Fill, Track, VectorShape } from '../types/project';

function uid(): string {
  return crypto.randomUUID();
}

const twoHueRadial: Fill = { kind: 'radial', stops: [
  { offset: 0, color: '$hueA' },
  { offset: 0.7, color: '$hueA' },
  { offset: 1, color: '$hueB' },
] };

const twoHueLinear: Fill = { kind: 'linear', angle: 0, stops: [
  { offset: 0, color: '$hueA' },
  { offset: 0.5, color: '$hueB' },
  { offset: 1, color: '$hueA' },
] };

export function vectorizeOrb(params: { radius: number }): VectorShape[] {
  return [
    {
      id: uid(),
      kind: 'ellipse',
      x: 0,
      y: 0,
      rx: params.radius,
      ry: params.radius,
      rotation: 0,
      fill: twoHueRadial,
      stroke: 'none',
      strokeWidth: 0,
      glow: { blur: params.radius * 0.5, color: '$hueA' },
    },
  ];
}

export function vectorizeRing(params: { radius: number; lineWidth: number }): VectorShape[] {
  return [
    {
      id: uid(),
      kind: 'ellipse',
      x: 0,
      y: 0,
      rx: params.radius,
      ry: params.radius,
      rotation: 0,
      fill: { kind: 'flat', color: 'none' },
      stroke: '$hueA',
      strokeWidth: params.lineWidth || 1.5,
      glow: { blur: 14, color: '$hueA' },
    },
  ];
}

export function vectorizeStreak(params: { length: number; thickness: number; mono: boolean }): VectorShape[] {
  return [
    {
      id: uid(),
      kind: 'rect',
      x: -params.length / 2,
      y: -params.thickness / 2,
      w: params.length,
      h: params.thickness,
      rotation: 0,
      fill: params.mono ? { kind: 'flat', color: '#ffffff' } : twoHueLinear,
      stroke: 'none',
      strokeWidth: 0,
      glow: { blur: params.thickness * 1.4, color: params.mono ? '#ffffff' : '$hueA' },
    },
  ];
}

// `spin` isn't shape geometry — it becomes a rotation TRACK (see
// bakeContinuousSpin below), applied by the caller onto the layer, not baked
// into these shapes.
export function vectorizeWheel(params: { radius: number; spokes: number; accentIdx: number }): VectorShape[] {
  const shapes: VectorShape[] = [];
  for (let i = 0; i < params.spokes; i++) {
    const a = i * ((Math.PI * 2) / params.spokes);
    const inner = params.radius * 0.06;
    const outer = params.radius * 1.28;
    const accent = i === params.accentIdx;
    shapes.push({
      id: uid(),
      kind: 'line',
      x1: Math.cos(a) * inner,
      y1: Math.sin(a) * inner,
      x2: Math.cos(a) * outer,
      y2: Math.sin(a) * outer,
      fill: { kind: 'flat', color: 'none' },
      stroke: accent ? '$hueA' : '#ffffffd9',
      strokeWidth: accent ? 3.2 : 1.4,
      glow: { blur: 10, color: accent ? '$hueA' : '#ffffff99' },
    });
  }
  shapes.push(...vectorizeRing({ radius: params.radius, lineWidth: 2.4 }));
  return shapes;
}

export function vectorizeShard(params: { size: number }): VectorShape[] {
  return [
    {
      id: uid(),
      kind: 'polygon',
      x: 0,
      y: 0,
      sides: 5,
      radius: params.size,
      rotation: 0,
      fill: { kind: 'flat', color: '#ffffff' },
      stroke: 'none',
      strokeWidth: 0,
      glow: { blur: params.size * 1.6, color: '#ffffff' },
    },
  ];
}

// The old ambientBeam drew 16 gradient strips spanning the FULL canvas width
// in absolute pixels, ignoring the layer's own x — a single wide gradient
// rect (well beyond any real canvas width) reproduces the "always fills the
// screen horizontally" look through the layer's normal transform instead.
export function vectorizeAmbientBeam(params: { bandHeight: number }): VectorShape[] {
  return [
    {
      id: uid(),
      kind: 'rect',
      x: -1000,
      y: -params.bandHeight / 2,
      w: 2000,
      h: params.bandHeight,
      rotation: 0,
      fill: twoHueLinear,
      stroke: 'none',
      strokeWidth: 0,
    },
  ];
}

export function vectorizeText(params: { text: string; fontSize: number; tracking: number }): VectorShape[] {
  return [
    {
      id: uid(),
      kind: 'text',
      x: 0,
      y: 0,
      text: params.text,
      fontSize: params.fontSize,
      tracking: params.tracking,
      fill: { kind: 'flat', color: '$hueA' },
      stroke: 'none',
      strokeWidth: 0,
      glow: { blur: 16, color: '$hueA' },
    },
  ];
}

// A single long linear sweep approximating "spin forever at a constant rate"
// (the old renderer's `rotation + nowMs * 0.001 * spin`) — a keyframe-only
// animation model has no way to express literal infinite angular velocity, so
// this bakes one sweep across the full song instead. Only meaningful for a
// layer with no pre-existing authored rotation keyframes (see call sites in
// migrate.ts/templates.ts) — an author-driven rotation swing takes
// precedence over the old implicit ambient spin rather than trying to
// compose the two.
export function bakeContinuousSpin(spinRadPerSec: number, songEnd: number): Track {
  return [
    { id: crypto.randomUUID(), t: 0, v: 0, ease: 'linear' },
    { id: crypto.randomUUID(), t: songEnd, v: spinRadPerSec * songEnd, ease: 'linear' },
  ];
}
