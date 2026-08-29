// The Lyrics Importer's non-UI core: parses a timed-lyrics file (LRC today —
// core/lrc.js's parseLRC/flattenWords; "any other timed format" is a future
// extension point, not built yet) and turns it into a GROUP of plain,
// individually-keyframed `vector` text layers instead of one special `lyrics`
// layer kind (which no longer exists — see types/project.ts's LayerType doc).
// Reused by three call sites: LyricsImporter.svelte's Commit button, and
// lib/migrate.ts's v3 upgrade for old `lyrics`-typed layers (via
// buildLyricsGroupFromWords, since an old save's stored words are already
// flattened — line-boundary info was lost at authoring time, so migration
// can't reconstruct "line" or "phrase" splitting, only faithfully reproduce
// the word-by-word reveal those layers actually rendered).
import type { AnyLayer, LyricWord } from '../types/project';
import { newLayer } from '../state/factories';
import { vectorizeText } from './vectorize';
import { parseLRC, flattenWords } from '../core/lrc.js';

export type LyricsSplitMode = 'line' | 'word' | 'phrase';
export type LyricsRevealMode = 'wordByWord' | 'lineHighlight';

export interface LyricsImportSettings {
  splitMode: LyricsSplitMode;
  phraseWordCount: number; // used only when splitMode === 'phrase'
  revealMode: LyricsRevealMode;
  x: number;
  y: number;
  fontSize: number;
  baseHue: number;
  highlightHue: number; // used only when revealMode === 'lineHighlight'
}

export const DEFAULT_LYRICS_SETTINGS: LyricsImportSettings = {
  splitMode: 'line',
  phraseWordCount: 3,
  revealMode: 'wordByWord',
  x: 135,
  y: 300,
  fontSize: 40,
  baseHue: 220,
  highlightHue: 45,
};

interface LrcLine {
  time: number;
  words: { text: string; time: number }[];
}

interface Chunk {
  text: string;
  start: number;
  end: number;
}

export function parseLyricsFile(raw: string): LrcLine[] {
  return parseLRC(raw) as LrcLine[];
}

function chunksForLines(lines: LrcLine[], settings: LyricsImportSettings, songEnd: number): Chunk[] {
  if (settings.splitMode === 'line') {
    return lines.map((line, i) => ({
      text: line.words.map((w) => w.text).join(' '),
      start: line.time,
      end: Math.min(lines[i + 1] ? lines[i + 1].time : line.time + 2.5, songEnd),
    }));
  }
  const flat = flattenWords(lines, {}) as LyricWord[];
  if (settings.splitMode === 'word') {
    return flat.map((w) => ({ text: w.text, start: w.time, end: Math.min(w.endTime, songEnd) }));
  }
  // phrase — group N consecutive flattened words together, ignoring line
  // boundaries (a phrase can straddle a line break, which is fine — it's a
  // deliberate "in-between" granularity, not a line re-grouping).
  const n = Math.max(1, settings.phraseWordCount);
  const chunks: Chunk[] = [];
  for (let i = 0; i < flat.length; i += n) {
    const group = flat.slice(i, i + n);
    chunks.push({ text: group.map((w) => w.text).join(' '), start: group[0].time, end: Math.min(group[group.length - 1].endTime, songEnd) });
  }
  return chunks;
}

// The shared per-chunk layer builder — one text shape, opacity fade in/out
// spanning [start,end]. `lineHighlight` additionally keyframes hueA as a
// base->highlight->base pulse across the chunk's own span: a per-CHUNK pulse,
// not true per-word highlighting underneath a full displayed line (that
// would need sub-chunk word timing even when splitMode groups multiple words
// together) — a deliberate, documented v1 simplification. Every keyframe
// this produces is a normal one, freely re-editable afterward like any other
// layer's.
function makeChunkLayer(text: string, start: number, end: number, opts: { x: number; y: number; fontSize: number; baseHue: number; highlightHue: number; revealMode: LyricsRevealMode }, index: number): AnyLayer {
  const layer = newLayer('vector');
  layer.name = text.trim() ? text.slice(0, 24) : `Lyric ${index + 1}`;
  layer.statics = { ...layer.statics, x: opts.x, y: opts.y, hueA: opts.baseHue, hueB: opts.baseHue };
  layer.params.shapes = vectorizeText({ text: text.toUpperCase(), fontSize: opts.fontSize, tracking: 2 });
  // visible stays true (the default) — the opacity track below is what
  // actually hides the layer outside its active window; evalVisible()/the
  // static `visible` flag is a separate on/off switch from opacity, not a
  // substitute for it.
  const fade = Math.min(0.1, Math.max(0.02, (end - start) / 4));
  layer.tracks.opacity = [
    { id: crypto.randomUUID(), t: Math.max(0, start - fade), v: 0, ease: 'linear' },
    { id: crypto.randomUUID(), t: start, v: 1, ease: 'linear' },
    { id: crypto.randomUUID(), t: Math.max(start, end - fade), v: 1, ease: 'linear' },
    { id: crypto.randomUUID(), t: end, v: 0, ease: 'linear' },
  ];
  if (opts.revealMode === 'lineHighlight') {
    const mid = (start + end) / 2;
    layer.tracks.hueA = [
      { id: crypto.randomUUID(), t: start, v: opts.baseHue, ease: 'linear' },
      { id: crypto.randomUUID(), t: mid, v: opts.highlightHue, ease: 'linear' },
      { id: crypto.randomUUID(), t: end, v: opts.baseHue, ease: 'linear' },
    ];
  }
  return layer;
}

// Full importer path — used by LyricsImporter.svelte's Commit button.
export function buildLyricsLayerGroup(lines: LrcLine[], settings: LyricsImportSettings, songEnd: number): AnyLayer[] {
  const chunks = chunksForLines(lines, settings, songEnd);
  return chunks.map((c, i) => makeChunkLayer(c.text, c.start, c.end, { ...settings, revealMode: settings.revealMode }, i));
}

// Migration-only path (lib/migrate.ts v3) — an old `lyrics` layer's stored
// `params.words` is already flattened (line-boundary info was lost when it
// was first imported), so this always reproduces word-by-word reveal, which
// is what those layers actually rendered at runtime anyway (renderLyricsLayer
// showed exactly one word at a time, keyed off playhead position).
export function buildLyricsGroupFromWords(
  words: LyricWord[],
  opts: { x: number; y: number; fontSize: number; baseHue: number; keyHue: number; songEnd: number }
): AnyLayer[] {
  return words.map((w, i) =>
    makeChunkLayer(
      w.text,
      w.time,
      Math.min(w.endTime, opts.songEnd),
      { x: opts.x, y: opts.y, fontSize: opts.fontSize, baseHue: w.isKey ? opts.keyHue : opts.baseHue, highlightHue: opts.keyHue, revealMode: 'wordByWord' },
      i
    )
  );
}
