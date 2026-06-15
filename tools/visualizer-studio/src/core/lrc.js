import { hash, seeded } from './hash.js';

// Supports both the plain Lyrics Timing Studio export ([mm:ss.xx]Full line text)
// and the word-level enhanced convention (<mm:ss.xx>word sub-tags). When no
// sub-tags are present, word timings are evenly distributed across the line.
export function parseLRC(raw) {
  const lines = [];
  raw.trim().split('\n').forEach((line) => {
    const lm = line.match(/^\[(\d+):(\d+\.\d+)\]\s*(.*)/);
    if (!lm) return;
    const lt = parseInt(lm[1]) * 60 + parseFloat(lm[2]);
    const rest = lm[3];
    const words = [];
    const re = /<(\d+):(\d+\.\d+)>([^\s<>]+)/g;
    let m;
    while ((m = re.exec(rest)) !== null) {
      words.push({ text: m[3], time: parseInt(m[1]) * 60 + parseFloat(m[2]) });
    }
    if (!words.length && rest.trim()) {
      // No word-level tags — split the raw line text evenly starting at lt;
      // real per-word timing gets filled in once we know the NEXT line's start.
      rest.trim().split(/\s+/).forEach((tok, i) => {
        words.push({ text: tok, time: lt, _auto: i });
      });
    }
    if (words.length) lines.push({ time: lt, words });
  });
  // Fill in auto-distributed word times using the gap to the next line.
  for (let li = 0; li < lines.length; li++) {
    const cur = lines[li];
    const next = lines[li + 1];
    const lineEnd = next ? next.time : cur.time + 2.5;
    const autoWords = cur.words.filter((w) => w._auto !== undefined);
    if (autoWords.length) {
      const span = Math.max(0.3, lineEnd - cur.time);
      autoWords.forEach((w) => {
        w.time = cur.time + (w._auto / autoWords.length) * span;
      });
    }
  }
  return lines;
}

export function flattenWords(lines, keySet) {
  const words = [];
  lines.forEach((line) => {
    line.words.forEach((w) => {
      const stripped = w.text.replace(/^[^\w']+|[^\w']+$/g, '').toLowerCase();
      words.push({ text: w.text, time: w.time, isKey: !!(keySet && keySet[stripped]) });
    });
  });
  for (let i = 0; i < words.length; i++) {
    const nextT = i + 1 < words.length ? words[i + 1].time : words[i].time + 2.0;
    words[i].endTime = Math.min(nextT, words[i].time + 2.0);
    words[i].seed = seeded(hash(`w${i}:${words[i].text}`))();
  }
  return words;
}
