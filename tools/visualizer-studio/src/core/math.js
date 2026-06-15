export function lerp(a, b, t) {
  return a + (b - a) * t;
}

export function clamp01(v) {
  return v < 0 ? 0 : v > 1 ? 1 : v;
}

// Shortest-path hue interpolation (never crosses the long way around the wheel).
export function lerpHue(a, b, t) {
  const d = ((b - a + 540) % 360) - 180;
  return (a + d * t + 360) % 360;
}
