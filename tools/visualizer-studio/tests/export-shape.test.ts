import { describe, expect, it } from 'vitest';
import { buildVisualizerHtml } from '../src/export/buildVisualizerHtml';
import { evalTrack } from '../src/core/ease';
import type { Project } from '../src/types/project';
import fixture from './fixtures/fixture-project.json';

const project = fixture as unknown as Project;

describe('buildVisualizerHtml — structure', () => {
  const html = buildVisualizerHtml(project);

  it('is a full HTML document with the expected host contract elements', () => {
    expect(html).toMatch(/^<!DOCTYPE html>/);
    expect(html).toContain('<canvas id="c"></canvas>');
    expect(html).toContain('<img id="cover-img" src="delta-asset:cover" crossorigin="anonymous">');
    expect(html).toContain('<title>Untitled - Visualizer</title>');
  });

  it('inlines the core modules with import/export syntax stripped', () => {
    expect(html).not.toMatch(/^\s*import\s/m);
    expect(html).not.toMatch(/^\s*export\s/m);
    // spot-check one function from each core module actually made it in
    expect(html).toContain('function lerp(');
    expect(html).toContain('function evalTrack(');
    expect(html).toContain('function drawWheelShape(');
    expect(html).toContain('function renderLayerAt(');
    expect(html).toContain('function sectionAt(');
  });

  it('omits the LRC parser entirely when the project has no lyrics layer', () => {
    expect(html).not.toContain('function parseLRC(');
    expect(html).not.toContain('function flattenWords(');
    expect(html).toContain('function getLyricWords(layer) { return []; }');
  });

  it('embeds the project with keyframe ids stripped', () => {
    const match = html.match(/var PROJECT=(\{.*?\});\n\nfunction getLyricWords/s);
    expect(match).toBeTruthy();
    const embedded = JSON.parse(match![1]);
    expect(embedded.layers).toHaveLength(3);
    for (const layer of embedded.layers) {
      for (const track of Object.values(layer.tracks) as any[]) {
        for (const kf of track) expect(kf.id).toBeUndefined();
      }
    }
  });

  it('preserves the host bridge contract (window.spectral hooks, delta-asset cover, file:// clock)', () => {
    expect(html).toContain('window.spectral.onPlaybackFrame');
    expect(html).toContain("window.location.protocol === 'file:'");
    expect(html).toContain('requestAnimationFrame(rafLoop)');
  });
});

describe('buildVisualizerHtml — with a lyrics layer', () => {
  const withLyrics: Project = structuredClone(project);
  withLyrics.layers.push({
    id: 'layer_lyrics',
    name: 'Lyrics',
    type: 'lyrics',
    visible: true,
    statics: { x: 135, y: 220, scale: 1, rotation: 0, opacity: 1, hueA: 220, hueB: 260 },
    tracks: { x: [], y: [], scale: [], rotation: [], opacity: [], hueA: [], hueB: [] },
    params: {
      placement: 'ANCHORED',
      fontSize: 64,
      tracking: 2,
      keyWords: '',
      suppressOnNoLyricsSections: true,
      words: [],
    },
  });
  const html = buildVisualizerHtml(withLyrics);

  it('includes the LRC parser and the host-substitution fallback to params.words', () => {
    expect(html).toContain('function parseLRC(');
    expect(html).toContain('function flattenWords(');
    expect(html).toContain('delta-data-json:untitled_lrc');
    expect(html).toContain('return layer.params.words || [];');
  });
});

describe('fixture-project.json — matches the old tool\'s Neon Edit template', () => {
  it('has the Wheel Burst / Venn Orb A / Venn Orb B layers in stacking order', () => {
    expect(project.layers.map((l) => l.name)).toEqual(['Wheel Burst', 'Venn Orb A', 'Venn Orb B']);
  });

  it('evaluates the wheel scale track at the build->drop cut the same way the old evalTrack would', () => {
    const wheel = project.layers[0];
    // Midway through the outcubic ramp from t=14.6 (v=0.2) to t=15.2 (v=1)
    const v = evalTrack(wheel.tracks.scale, 14.9, wheel.statics.scale);
    expect(v).toBeGreaterThan(0.2);
    expect(v).toBeLessThan(1);
    // Held flat between the two "up" keyframes (44 -> 45 incubic ramp starts at 44)
    expect(evalTrack(wheel.tracks.scale, 30, wheel.statics.scale)).toBe(1);
  });

  it('Drop section is flagged noLyrics, matching sections[1].noLyrics=true in the old template', () => {
    expect(project.sections.find((s) => s.id === 'drop')?.noLyrics).toBe(true);
  });
});
