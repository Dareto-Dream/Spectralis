import type { Project } from '../types/project';
import { newLayer, defaultSection, newProject } from '../state/factories';
import { vectorizeOrb, vectorizeWheel, vectorizeText } from './vectorize';

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
