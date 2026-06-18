import type { Project } from '../types/project';

// Upgrades old formatVersion:1 saves (renames the two scratch fields that used
// to be bolted onto `project._importedWords`/`_importedLrcRaw`) and, defensively,
// backfills a stable id onto any keyframe that doesn't have one — old-format
// saves never had keyframe ids at all. Throws with a message meant to be shown
// directly to the user (TopBar's Load Project error dialog), not just logged.
export function migrateProject(raw: unknown): Project {
  if (!raw || typeof raw !== 'object') throw new Error('not a JSON object');
  const obj = raw as Record<string, unknown>;
  if (!obj.meta || !Array.isArray(obj.sections) || !Array.isArray(obj.layers)) {
    throw new Error('missing meta/sections/layers — this doesn\'t look like a Studio project file');
  }

  const project = { ...obj } as Record<string, unknown>;
  if (project.formatVersion !== 2) {
    if (project._importedWords !== undefined) {
      project.importedWords = project._importedWords;
      delete project._importedWords;
    }
    if (project._importedLrcRaw !== undefined) {
      project.importedLrcRaw = project._importedLrcRaw;
      delete project._importedLrcRaw;
    }
    project.formatVersion = 2;
  }

  for (const layer of project.layers as Array<Record<string, unknown>>) {
    const tracks = layer.tracks as Record<string, Array<Record<string, unknown>>> | undefined;
    if (!tracks) continue;
    for (const key of Object.keys(tracks)) {
      tracks[key] = (tracks[key] || []).map((kf) => (kf.id ? kf : { ...kf, id: crypto.randomUUID() }));
    }
  }

  return project as unknown as Project;
}
