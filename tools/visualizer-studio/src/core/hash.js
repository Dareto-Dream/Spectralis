export function hash(value) {
  let h = 2166136261;
  value = String(value);
  for (let i = 0; i < value.length; i++) {
    h ^= value.charCodeAt(i);
    h = Math.imul(h, 16777619);
  }
  return h >>> 0;
}

// Deterministic PRNG (mulberry-ish LCG) seeded from hash() — used for word jitter
// so re-rendering the same lyric word always jitters identically.
export function seeded(seed) {
  let s = seed >>> 0;
  return function () {
    s = (s * 1664525 + 1013904223) >>> 0;
    return s / 4294967296;
  };
}
