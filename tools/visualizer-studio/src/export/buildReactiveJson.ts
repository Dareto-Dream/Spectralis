import type { Project } from '../types/project';

export interface ReactiveEvent {
  type: 'set' | 'transition';
  t: number;
  target: string;
  duration?: number;
  easing?: string;
  params: { accentHue: number; intensity: number; scene: string };
}

// Gap 4: the old tool's buildReactiveJson didn't synthesize real timeline events
// at all — it emitted a `sections` array with a human-readable `mood` STRING
// ("hue 190→90, intensity 0.5"), nothing resembling the spectralis-track-reactive
// format's actual `timeline` event array. This synthesizes one `set` event at the
// first section's start and one `transition` per subsequent section boundary,
// grounded against real hand-authored *_reactive.json files. `scene` reuses the
// section's own id (already slug-like, matches real files' convention).
//
// Known limitation: real files also contain artisanal mid-section "mood beat"
// events with no section-boundary alignment. Studio has no data model for these
// and this synthesizer correctly doesn't try to invent them — expected partial
// parity, not a bug.
export function buildReactiveEvents(project: Project): ReactiveEvent[] {
  const sections = project.sections;
  if (!sections.length) return [];
  const events: ReactiveEvent[] = [
    {
      type: 'set',
      t: sections[0].start,
      target: 'theme',
      params: { accentHue: sections[0].hue, intensity: sections[0].intensity, scene: sections[0].id },
    },
  ];
  for (let i = 1; i < sections.length; i++) {
    const s = sections[i];
    events.push({
      type: 'transition',
      t: s.start,
      target: 'theme',
      duration: 1.5,
      easing: 'inoutcubic',
      params: { accentHue: s.hue, intensity: s.intensity, scene: s.id },
    });
  }
  return events;
}

export function buildReactiveJson(project: Project): string {
  return JSON.stringify(
    {
      format: 'spectralis-track-reactive',
      formatVersion: 3,
      timeline: buildReactiveEvents(project),
    },
    null,
    2
  );
}
