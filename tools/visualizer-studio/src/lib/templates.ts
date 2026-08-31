import type { AnyLayer, Project, VectorShape } from '../types/project';
import { newLayer, defaultSection, newProject } from '../state/factories';
import {
  vectorizeOrb,
  vectorizeRing,
  vectorizeStreak,
  vectorizeWheel,
  vectorizeShard,
  vectorizeAmbientBeam,
  vectorizeText,
  bakeContinuousSpin,
} from './vectorize';

export interface Template {
  id: string;
  label: string;
  hint: string;
  build: () => Project;
}

// Rebuilt on top of lib/vectorize.ts's converters instead of newLayer('wheel')/
// newLayer('orb') — those kinds don't exist anymore (see types/project.ts's
// LayerType doc). Same reasoning as v3 migration in lib/migrate.ts: one
// source of "what an orb/wheel/etc. actually looks like as vector content",
// reused by both, so they can't drift apart.
function neonEdit(): Project {
  const project = newProject();
  project.meta.songEnd = 60;
  project.sections = [
    defaultSection('build', 'Build', 0, 15, 300, 30),
    defaultSection('drop', 'Drop', 15, 45, 190, 90),
    defaultSection('outro', 'Outro', 45, 60, 30, 15),
  ];
  project.sections[1].noLyrics = true;

  const wheel = newLayer('vector');
  wheel.name = 'Wheel Burst';
  wheel.statics = { x: 135, y: 210, scale: 0.2, rotation: 0, opacity: 0, hueA: 320, hueB: 30 };
  wheel.tracks.scale = [
    { id: crypto.randomUUID(), t: 14.6, v: 0.2, ease: 'outcubic' },
    { id: crypto.randomUUID(), t: 15.2, v: 1, ease: 'linear' },
    { id: crypto.randomUUID(), t: 44, v: 1, ease: 'incubic' },
    { id: crypto.randomUUID(), t: 45, v: 0.3, ease: 'linear' },
  ];
  wheel.tracks.opacity = [
    { id: crypto.randomUUID(), t: 14.6, v: 0, ease: 'outcubic' },
    { id: crypto.randomUUID(), t: 15.1, v: 1, ease: 'linear' },
    { id: crypto.randomUUID(), t: 44.5, v: 1, ease: 'incubic' },
    { id: crypto.randomUUID(), t: 45, v: 0, ease: 'linear' },
  ];
  // Authored rotation swing takes the place of the old implicit "ambient
  // spin" — see vectorize.ts's bakeContinuousSpin doc for why the two don't
  // compose in a keyframe-only model.
  wheel.tracks.rotation = [
    { id: crypto.randomUUID(), t: 15, v: 0, ease: 'linear' },
    { id: crypto.randomUUID(), t: 45, v: 6, ease: 'linear' },
  ];
  wheel.params.shapes = vectorizeWheel({ radius: 90, spokes: 14, accentIdx: 0 });
  project.layers.push(wheel);

  const orb1 = newLayer('vector');
  orb1.name = 'Venn Orb A';
  orb1.statics = { x: 95, y: 230, scale: 0, rotation: 0, opacity: 0.9, hueA: 190, hueB: 90 };
  orb1.tracks.scale = [
    { id: crypto.randomUUID(), t: 15, v: 0, ease: 'outcubic' },
    { id: crypto.randomUUID(), t: 15.4, v: 1, ease: 'linear' },
    { id: crypto.randomUUID(), t: 45, v: 1, ease: 'incubic' },
    { id: crypto.randomUUID(), t: 45.6, v: 0, ease: 'linear' },
  ];
  orb1.params.shapes = vectorizeOrb({ radius: 60 });
  project.layers.push(orb1);

  const orb2 = newLayer('vector');
  orb2.name = 'Venn Orb B';
  orb2.statics = { x: 175, y: 250, scale: 0, rotation: 0, opacity: 0.9, hueA: 90, hueB: 190 };
  orb2.tracks.scale = [
    { id: crypto.randomUUID(), t: 15.1, v: 0, ease: 'outcubic' },
    { id: crypto.randomUUID(), t: 15.5, v: 1, ease: 'linear' },
    { id: crypto.randomUUID(), t: 45, v: 1, ease: 'incubic' },
    { id: crypto.randomUUID(), t: 45.6, v: 0, ease: 'linear' },
  ];
  orb2.params.shapes = vectorizeOrb({ radius: 60 });
  project.layers.push(orb2);

  return project;
}

function kineticTypography(): Project {
  const project = newProject();
  project.meta.songEnd = 180;
  project.sections = [
    defaultSection('intro', 'Intro', 0, 16, 220, 240),
    defaultSection('verse', 'Verse', 16, 60, 250, 266),
    defaultSection('chorus', 'Chorus', 60, 90, 338, 20),
    defaultSection('outro', 'Outro', 90, 180, 26, 15),
  ];

  // Placeholder text — real word-by-word/line lyrics come from Tools >
  // Lyrics Importer against your own .lrc file, which generates a whole
  // group of individually-keyframed vector text layers instead of one
  // static placeholder like this.
  const lyr = newLayer('vector');
  lyr.name = 'Lyrics Placeholder';
  lyr.params.shapes = vectorizeText({ text: 'YOUR LYRICS HERE', fontSize: 48, tracking: 2 });
  project.layers.push(lyr);

  const orb = newLayer('vector');
  orb.name = 'Ambient Orb';
  orb.statics = { x: 135, y: 400, scale: 1, rotation: 0, opacity: 0.5, hueA: 230, hueB: 250 };
  orb.tracks.opacity = [
    { id: crypto.randomUUID(), t: 0, v: 0.15, ease: 'outsine' },
    { id: crypto.randomUUID(), t: 60, v: 0.4, ease: 'outsine' },
    { id: crypto.randomUUID(), t: 120, v: 0.15, ease: 'linear' },
  ];
  orb.params.shapes = vectorizeOrb({ radius: 60 });
  project.layers.unshift(orb);

  return project;
}

// Non-destructive LAYER templates — the actual "orb/ring/streak/spin wheel/
// ambient beam/shard" starting points the user meant when they said
// Templates, as opposed to the whole-project TEMPLATES above (which REPLACE
// the current project). Dropped into the current project via
// store.addLayers(tpl.build(songEnd)) — see AssetsPanel.svelte's "Layers"
// template sub-tab. Built on the exact same lib/vectorize.ts converters v3
// migration uses, so a template and a migrated old save of the same "look"
// always render identically.
export interface LayerTemplate {
  id: string;
  label: string;
  hint: string;
  // Takes the current project's songEnd — only the Spin Wheel needs it (to
  // bake a full-song rotation sweep, see vectorize.ts's bakeContinuousSpin
  // doc), everything else ignores the argument.
  build: (songEnd: number) => AnyLayer[];
}

function vectorLayerFrom(name: string, shapes: VectorShape[], hue: { hueA: number; hueB: number }): AnyLayer {
  const layer = newLayer('vector');
  layer.name = name;
  layer.statics.hueA = hue.hueA;
  layer.statics.hueB = hue.hueB;
  layer.params.shapes = shapes;
  return layer;
}

export const LAYER_TEMPLATES: LayerTemplate[] = [
  {
    id: 'layer-orb',
    label: 'Orb',
    hint: 'Soft glowing hue-cycling orb',
    build: () => [vectorLayerFrom('Orb', vectorizeOrb({ radius: 60 }), { hueA: 190, hueB: 90 })],
  },
  {
    id: 'layer-ring',
    label: 'Ring',
    hint: 'Thin glowing ring outline',
    build: () => [vectorLayerFrom('Ring', vectorizeRing({ radius: 70, lineWidth: 2 }), { hueA: 200, hueB: 320 })],
  },
  {
    id: 'layer-streak',
    label: 'Streak',
    hint: 'Horizontal glowing hue-gradient streak',
    build: () => [vectorLayerFrom('Streak', vectorizeStreak({ length: 200, thickness: 10, mono: false }), { hueA: 30, hueB: 300 })],
  },
  {
    id: 'layer-spin-wheel',
    label: 'Spin Wheel',
    hint: 'Spoked wheel that spins continuously',
    build: (songEnd) => {
      const layer = vectorLayerFrom('Spin Wheel', vectorizeWheel({ radius: 90, spokes: 12, accentIdx: 0 }), { hueA: 320, hueB: 30 });
      layer.tracks.rotation = bakeContinuousSpin(0.6, songEnd || 60);
      return [layer];
    },
  },
  {
    id: 'layer-ambient-beam',
    label: 'Ambient Beam',
    hint: 'Full-width soft gradient beam band',
    build: () => [vectorLayerFrom('Ambient Beam', vectorizeAmbientBeam({ bandHeight: 60 }), { hueA: 210, hueB: 280 })],
  },
  {
    id: 'layer-shard',
    label: 'Shard',
    hint: 'Faceted glowing polygon shard',
    build: () => [vectorLayerFrom('Shard', vectorizeShard({ size: 50 }), { hueA: 0, hueB: 0 })],
  },
];

export const TEMPLATES: Template[] = [
  {
    id: 'neon-edit',
    label: 'Neon Edit',
    hint: 'Orbs + wheel burst + streaks, AE-style cuts',
    build: neonEdit,
  },
  {
    id: 'kinetic-typography',
    label: 'Kinetic Typography',
    hint: 'Verse/chorus, hue-shift, text-forward — pair with Tools > Lyrics Importer',
    build: kineticTypography,
  },
  {
    id: 'blank',
    label: 'Blank',
    hint: 'Empty project',
    build: newProject,
  },
];
