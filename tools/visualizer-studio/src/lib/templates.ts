import type { Project } from '../types/project';
import { newLayer, defaultSection, newProject } from '../state/factories';

export interface Template {
  id: string;
  label: string;
  hint: string;
  build: () => Project;
}

function neonEdit(): Project {
  const project = newProject();
  project.meta.songEnd = 60;
  project.sections = [
    defaultSection('build', 'Build', 0, 15, 300, 30),
    defaultSection('drop', 'Drop', 15, 45, 190, 90),
    defaultSection('outro', 'Outro', 45, 60, 30, 15),
  ];
  project.sections[1].noLyrics = true;

  const wheel = newLayer('wheel');
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
  wheel.tracks.rotation = [
    { id: crypto.randomUUID(), t: 15, v: 0, ease: 'linear' },
    { id: crypto.randomUUID(), t: 45, v: 6, ease: 'linear' },
  ];
  project.layers.push(wheel);

  const orb1 = newLayer('orb');
  orb1.name = 'Venn Orb A';
  orb1.statics = { x: 95, y: 230, scale: 0, rotation: 0, opacity: 0.9, hueA: 190, hueB: 90 };
  orb1.tracks.scale = [
    { id: crypto.randomUUID(), t: 15, v: 0, ease: 'outcubic' },
    { id: crypto.randomUUID(), t: 15.4, v: 1, ease: 'linear' },
    { id: crypto.randomUUID(), t: 45, v: 1, ease: 'incubic' },
    { id: crypto.randomUUID(), t: 45.6, v: 0, ease: 'linear' },
  ];
  project.layers.push(orb1);

  const orb2 = newLayer('orb');
  orb2.name = 'Venn Orb B';
  orb2.statics = { x: 175, y: 250, scale: 0, rotation: 0, opacity: 0.9, hueA: 90, hueB: 190 };
  orb2.tracks.scale = [
    { id: crypto.randomUUID(), t: 15.1, v: 0, ease: 'outcubic' },
    { id: crypto.randomUUID(), t: 15.5, v: 1, ease: 'linear' },
    { id: crypto.randomUUID(), t: 45, v: 1, ease: 'incubic' },
    { id: crypto.randomUUID(), t: 45.6, v: 0, ease: 'linear' },
  ];
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

  const lyr = newLayer('lyrics');
  lyr.params.placement = 'ANCHORED';
  project.layers.push(lyr);

  const orb = newLayer('orb');
  orb.name = 'Ambient Orb';
  orb.statics = { x: 135, y: 400, scale: 1, rotation: 0, opacity: 0.5, hueA: 230, hueB: 250 };
  orb.tracks.opacity = [
    { id: crypto.randomUUID(), t: 0, v: 0.15, ease: 'outsine' },
    { id: crypto.randomUUID(), t: 60, v: 0.4, ease: 'outsine' },
    { id: crypto.randomUUID(), t: 120, v: 0.15, ease: 'linear' },
  ];
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
    hint: 'Verse/chorus, hue-shift, text-forward',
    build: kineticTypography,
  },
  {
    id: 'blank',
    label: 'Blank',
    hint: 'Empty project',
    build: newProject,
  },
];
