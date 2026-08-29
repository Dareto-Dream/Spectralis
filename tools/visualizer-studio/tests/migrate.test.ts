import { describe, expect, it } from 'vitest';
import { migrateProject } from '../src/lib/migrate';

function emptyTracks() {
  return { x: [], y: [], scale: [], rotation: [], opacity: [], hueA: [], hueB: [] };
}
function statics() {
  return { x: 135, y: 220, scale: 1, rotation: 0, opacity: 1, hueA: 220, hueB: 260 };
}

// A hand-built v2 fixture with one of each legacy kind (plus a lyrics layer)
// — the exact shape a pre-rework save on disk would have had.
function v2Fixture() {
  return {
    formatVersion: 2,
    meta: { title: 'T', artist: '', slug: 't', songEnd: 60, aspect: '9x16' },
    sections: [{ id: 's1', label: 'S1', start: 0, end: 60, hue: 0, hue2: 0, intensity: 0.5, noLyrics: false }],
    layers: [
      { id: 'l_orb', name: 'Orb', type: 'orb', visible: true, statics: statics(), tracks: emptyTracks(), params: { radius: 60 } },
      { id: 'l_ring', name: 'Ring', type: 'ring', visible: true, statics: statics(), tracks: emptyTracks(), params: { radius: 80, lineWidth: 2 } },
      {
        id: 'l_streak',
        name: 'Streak',
        type: 'streak',
        visible: true,
        statics: statics(),
        tracks: emptyTracks(),
        params: { length: 300, thickness: 14, mono: false },
      },
      {
        id: 'l_wheel',
        name: 'Wheel',
        type: 'wheel',
        visible: true,
        statics: statics(),
        tracks: emptyTracks(), // no authored rotation -> spin should get baked in
        params: { radius: 90, spokes: 14, spin: 0.35, accentIdx: 0 },
      },
      {
        id: 'l_shard',
        name: 'Shard',
        type: 'shard',
        visible: true,
        statics: statics(),
        tracks: emptyTracks(),
        params: { size: 10, spin: 0.6 },
      },
      {
        id: 'l_beam',
        name: 'Beam',
        type: 'ambientBeam',
        visible: true,
        statics: statics(),
        tracks: emptyTracks(),
        params: { bandHeight: 120 },
      },
      {
        id: 'l_text',
        name: 'Text',
        type: 'text',
        visible: true,
        statics: statics(),
        tracks: emptyTracks(),
        params: { text: 'TEXT', fontSize: 48, tracking: 2 },
      },
      {
        id: 'l_lyrics',
        name: 'Lyrics',
        type: 'lyrics',
        visible: true,
        statics: statics(),
        tracks: emptyTracks(),
        params: {
          placement: 'ANCHORED',
          fontSize: 64,
          tracking: 2,
          keyWords: '',
          suppressOnNoLyricsSections: true,
          words: [
            { text: 'hey', time: 1, endTime: 2, isKey: false, seed: 0 },
            { text: 'yo', time: 2, endTime: 3, isKey: true, seed: 0 },
          ],
        },
      },
    ],
  };
}

describe('migrate.ts — v2 -> v3 (legacy layer kinds to bitmap/vector)', () => {
  const project = migrateProject(v2Fixture());

  it('bumps formatVersion to 3', () => {
    expect(project.formatVersion).toBe(3);
  });

  it('every one-to-one converted kind becomes a vector layer, id/tracks/statics untouched', () => {
    const orb = project.layers.find((l) => l.id === 'l_orb')!;
    expect(orb.type).toBe('vector');
    expect(orb.statics).toEqual(statics());
    if (orb.type === 'vector') expect(orb.params.shapes[0].kind).toBe('ellipse');
  });

  it('a wheel/shard with no authored rotation keyframes gets a baked continuous-spin rotation track', () => {
    const wheel = project.layers.find((l) => l.id === 'l_wheel')!;
    expect(wheel.tracks.rotation.length).toBeGreaterThan(0);
    expect(wheel.tracks.rotation[0].v).toBe(0);
  });

  it('the lyrics layer is gone, replaced by a group of vector layers tagged with a "Lyrics" LayerGroup', () => {
    expect(project.layers.some((l) => l.id === 'l_lyrics')).toBe(false);
    const generated = project.layers.filter((l) => l.name === 'hey' || l.name === 'yo');
    expect(generated).toHaveLength(2);
    expect(generated.every((l) => l.type === 'vector')).toBe(true);
    const groupIds = new Set(generated.map((l) => l.groupId));
    expect(groupIds.size).toBe(1);
    expect(project.layerGroups?.find((g) => g.id === [...groupIds][0])?.name).toBe('Lyrics');
  });

  it('no layer keeps a legacy type — the union genuinely shrinks to bitmap/vector', () => {
    for (const layer of project.layers) {
      expect(['bitmap', 'vector']).toContain(layer.type);
    }
  });

  it('re-migrating an already-v3 project is a no-op on layer count/types', () => {
    const again = migrateProject(project);
    expect(again.layers).toHaveLength(project.layers.length);
    expect(again.layers.map((l) => l.type)).toEqual(project.layers.map((l) => l.type));
  });
});
