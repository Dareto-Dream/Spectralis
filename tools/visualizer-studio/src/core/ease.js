import { lerp, lerpHue } from './math.js';

export const EASE = {
  hold: () => 0,
  linear: (t) => t,
  incubic: (t) => t * t * t,
  outcubic: (t) => {
    const p = 1 - t;
    return 1 - p * p * p;
  },
  inoutcubic: (t) => (t < 0.5 ? 4 * t * t * t : 1 - Math.pow(-2 * t + 2, 3) / 2),
  insine: (t) => 1 - Math.cos((t * Math.PI) / 2),
  outsine: (t) => Math.sin((t * Math.PI) / 2),
};

// Evaluate an animatable track (array of {t,v,ease}) at time `t`.
// `hueMode` uses shortest-path hue interpolation instead of linear lerp.
// `ease` on a keyframe is the OUTGOING easing for the segment that starts there.
export function evalTrack(track, t, dflt, hueMode) {
  if (!track || !track.length) return dflt;
  if (track.length === 1) return track[0].v;
  if (t <= track[0].t) return track[0].v;
  if (t >= track[track.length - 1].t) return track[track.length - 1].v;
  for (let i = 0; i < track.length - 1; i++) {
    const a = track[i];
    const b = track[i + 1];
    if (t >= a.t && t <= b.t) {
      const span = b.t - a.t;
      const f = span > 0 ? (t - a.t) / span : 1;
      if (a.ease === 'hold') return a.v;
      const eased = (EASE[a.ease] || EASE.linear)(f);
      return hueMode ? lerpHue(a.v, b.v, eased) : lerp(a.v, b.v, eased);
    }
  }
  return dflt;
}
