import { describe, expect, it } from 'vitest';
import { buildLyricsLayerGroup, buildLyricsGroupFromWords, parseLyricsFile, DEFAULT_LYRICS_SETTINGS } from '../src/lib/lyricsImport';

const SAMPLE_LRC = ['[00:01.00]Hello there world', '[00:03.50]It is a good day'].join('\n');

describe('lyricsImport.ts — buildLyricsLayerGroup', () => {
  const lines = parseLyricsFile(SAMPLE_LRC);

  it('parses the sample into 2 timed lines', () => {
    expect(lines).toHaveLength(2);
    expect(lines[0].time).toBeCloseTo(1, 3);
  });

  it('splitMode "line" produces one vector layer per line, each a chunk of that line\'s words', () => {
    const layers = buildLyricsLayerGroup(lines, { ...DEFAULT_LYRICS_SETTINGS, splitMode: 'line' }, 60);
    expect(layers).toHaveLength(2);
    expect(layers.every((l) => l.type === 'vector')).toBe(true);
    expect(layers[0].name).toContain('Hello there world'.slice(0, 24));
  });

  it('splitMode "word" produces one layer per individual word', () => {
    const layers = buildLyricsLayerGroup(lines, { ...DEFAULT_LYRICS_SETTINGS, splitMode: 'word' }, 60);
    expect(layers).toHaveLength(8); // "Hello there world" (3) + "It is a good day" (5)
  });

  it('splitMode "phrase" groups N words per layer', () => {
    const layers = buildLyricsLayerGroup(lines, { ...DEFAULT_LYRICS_SETTINGS, splitMode: 'phrase', phraseWordCount: 3 }, 60);
    expect(layers).toHaveLength(3); // ceil(8/3)
  });

  it('revealMode "wordByWord" keyframes a fade in/out on opacity only', () => {
    const [layer] = buildLyricsLayerGroup(lines, { ...DEFAULT_LYRICS_SETTINGS, splitMode: 'line', revealMode: 'wordByWord' }, 60);
    expect(layer.tracks.opacity.length).toBeGreaterThan(0);
    expect(layer.tracks.hueA).toHaveLength(0);
  });

  it('revealMode "lineHighlight" additionally keyframes hueA as a base->highlight->base pulse', () => {
    const [layer] = buildLyricsLayerGroup(lines, { ...DEFAULT_LYRICS_SETTINGS, splitMode: 'line', revealMode: 'lineHighlight', highlightHue: 45 }, 60);
    expect(layer.tracks.hueA.map((k) => k.v)).toEqual([DEFAULT_LYRICS_SETTINGS.baseHue, 45, DEFAULT_LYRICS_SETTINGS.baseHue]);
  });

  it('every generated layer is a plain vector layer with a single text shape — fully editable afterward, nothing special', () => {
    const layers = buildLyricsLayerGroup(lines, DEFAULT_LYRICS_SETTINGS, 60);
    for (const layer of layers) {
      expect(layer.type).toBe('vector');
      if (layer.type === 'vector') {
        expect(layer.params.shapes).toHaveLength(1);
        expect(layer.params.shapes[0].kind).toBe('text');
      }
    }
  });
});

describe('lyricsImport.ts — buildLyricsGroupFromWords (migration path)', () => {
  it('produces one word-by-word layer per stored LyricWord, keyed off its own time/endTime', () => {
    const words = [
      { text: 'hey', time: 1, endTime: 2, isKey: false, seed: 0 },
      { text: 'yo', time: 2, endTime: 3, isKey: true, seed: 0 },
    ];
    const layers = buildLyricsGroupFromWords(words, { x: 135, y: 300, fontSize: 48, baseHue: 220, keyHue: 45, songEnd: 60 });
    expect(layers).toHaveLength(2);
    expect(layers[0].statics.hueA).toBe(220); // not a key word
    expect(layers[1].statics.hueA).toBe(45); // isKey -> keyHue
  });
});
