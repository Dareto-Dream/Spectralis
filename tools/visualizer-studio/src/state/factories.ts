import type { Layer, LayerType, Project, Section, Statics, Tracks } from '../types/project';
import { ANIM_KEYS } from '../types/project';
import { LAYER_TYPE_DEFS } from '../types/layerDefs';

function uid(prefix: string): string {
  return `${prefix}_${crypto.randomUUID()}`;
}

export function newKeyframeId(): string {
  return uid('kf');
}

export function emptyTracks(): Tracks {
  const tracks = {} as Tracks;
  for (const key of ANIM_KEYS) tracks[key] = [];
  return tracks;
}

export function defaultStatics(): Statics {
  return { x: 135, y: 220, scale: 1, rotation: 0, opacity: 1, hueA: 220, hueB: 260 };
}

export function newLayer<T extends LayerType>(type: T): Layer<T> {
  const def = LAYER_TYPE_DEFS[type];
  return {
    id: uid('layer'),
    name: def.defaultName,
    type,
    visible: true,
    tracks: emptyTracks(),
    statics: defaultStatics(),
    params: def.defaultParams(),
    visibleTrack: [],
  } as Layer<T>;
}

export function defaultSection(id: string, label: string, start: number, end: number, hue: number, hue2: number): Section {
  return { id, label, start, end, hue, hue2, intensity: 0.5, noLyrics: false };
}

export function newProject(): Project {
  return {
    formatVersion: 2,
    meta: { title: 'Untitled', artist: '', slug: 'untitled', songEnd: 60, aspect: '9x16' },
    sections: [defaultSection('s1', 'Section 1', 0, 60, 220, 260)],
    layers: [],
  };
}
