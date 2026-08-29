import type { Project } from '../types/project';
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
import { buildLyricsGroupFromWords } from './lyricsImport';

// Upgrades old formatVersion:1 saves (renames the two scratch fields that used
// to be bolted onto `project._importedWords`/`_importedLrcRaw`), formatVersion:2
// saves (the old 8-kind `LayerType` union — orb/ring/streak/wheel/ambientBeam/
// shard/text/lyrics — collapses to just `bitmap`/`vector`, see types/
// project.ts), and, defensively, backfills a stable id onto any keyframe that
// doesn't have one — old-format saves never had keyframe ids at all. Throws
// with a message meant to be shown directly to the user (TopBar's Load
// Project error dialog), not just logged.
export function migrateProject(raw: unknown): Project {
  if (!raw || typeof raw !== 'object') throw new Error('not a JSON object');
  const obj = raw as Record<string, unknown>;
  if (!obj.meta || !Array.isArray(obj.sections) || !Array.isArray(obj.layers)) {
    throw new Error('missing meta/sections/layers — this doesn\'t look like a Studio project file');
  }

  const project = { ...obj } as Record<string, unknown>;
  if (project.formatVersion !== 2 && project.formatVersion !== 3) {
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

  if (project.formatVersion === 2) {
    migrateLayersToVectorBitmap(project);
    project.formatVersion = 3;
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

// v2 -> v3: the old fixed 8-kind LayerType union collapses to bitmap/vector.
// Every legacy kind gets converted to an equivalent `vector` layer in place —
// id/tracks/statics/visible/locked/visibleTrack are left completely alone
// (only `type`/`params` change), so a layer's existing keyframed motion on
// the generic transform keeps working exactly as authored. `lyrics` layers
// expand into a whole GROUP of layers (one per word — see
// buildLyricsGroupFromWords's doc for why word-level, not line-level), so
// they're spliced out in favor of the generated group at the same array
// position instead of converted 1:1.
function migrateLayersToVectorBitmap(project: Record<string, unknown>): void {
  const songEnd = (project.meta as { songEnd?: number })?.songEnd ?? 60;
  const layers = project.layers as Array<Record<string, unknown>>;
  const groups: Array<{ id: string; name: string }> = Array.isArray(project.layerGroups) ? (project.layerGroups as any) : [];
  const nextLayers: Array<Record<string, unknown>> = [];

  for (const layer of layers) {
    const type = layer.type as string;
    const params = (layer.params ?? {}) as Record<string, unknown>;
    const rotationTrack = (layer.tracks as Record<string, unknown[]> | undefined)?.rotation ?? [];

    switch (type) {
      case 'orb':
        nextLayers.push({ ...layer, type: 'vector', params: { shapes: vectorizeOrb(params as { radius: number }) } });
        break;
      case 'ring':
        nextLayers.push({ ...layer, type: 'vector', params: { shapes: vectorizeRing(params as { radius: number; lineWidth: number }) } });
        break;
      case 'streak':
        nextLayers.push({ ...layer, type: 'vector', params: { shapes: vectorizeStreak(params as { length: number; thickness: number; mono: boolean }) } });
        break;
      case 'text':
        nextLayers.push({ ...layer, type: 'vector', params: { shapes: vectorizeText(params as { text: string; fontSize: number; tracking: number }) } });
        break;
      case 'wheel': {
        const wheelParams = params as { radius: number; spokes: number; spin: number; accentIdx: number };
        const migrated: Record<string, unknown> = { ...layer, type: 'vector', params: { shapes: vectorizeWheel(wheelParams) } };
        applySpinIfNoAuthoredRotation(migrated, rotationTrack, wheelParams.spin, songEnd);
        nextLayers.push(migrated);
        break;
      }
      case 'shard': {
        const shardParams = params as { size: number; spin: number };
        const migrated: Record<string, unknown> = { ...layer, type: 'vector', params: { shapes: vectorizeShard(shardParams) } };
        applySpinIfNoAuthoredRotation(migrated, rotationTrack, shardParams.spin, songEnd);
        nextLayers.push(migrated);
        break;
      }
      case 'ambientBeam':
        nextLayers.push({ ...layer, type: 'vector', params: { shapes: vectorizeAmbientBeam(params as { bandHeight: number }) } });
        break;
      case 'lyrics': {
        const lyricsParams = params as { words?: Array<{ text: string; time: number; endTime: number; isKey: boolean; seed: number }>; fontSize?: number };
        const words = Array.isArray(lyricsParams.words) ? lyricsParams.words : [];
        const statics = layer.statics as { x: number; y: number; hueA: number } | undefined;
        const group = buildLyricsGroupFromWords(words, {
          x: statics?.x ?? 135,
          y: statics?.y ?? 300,
          fontSize: lyricsParams.fontSize ?? 48,
          baseHue: statics?.hueA ?? 220,
          keyHue: 45,
          songEnd,
        });
        if (group.length) {
          const groupId = `group_${crypto.randomUUID()}`;
          groups.push({ id: groupId, name: 'Lyrics' });
          for (const g of group) nextLayers.push({ ...g, groupId } as unknown as Record<string, unknown>);
        }
        break;
      }
      default:
        // Already bitmap/vector (a v3 save re-migrated, or a stray unknown
        // kind) — pass through untouched rather than dropping it.
        nextLayers.push(layer);
    }
  }

  project.layers = nextLayers;
  if (groups.length) project.layerGroups = groups;
}

// See vectorize.ts's bakeContinuousSpin doc — an authored rotation swing
// (any pre-existing keyframes) takes precedence over the old implicit
// "ambient spin" rather than trying to compose the two.
function applySpinIfNoAuthoredRotation(layer: Record<string, unknown>, rotationTrack: unknown[], spin: number, songEnd: number): void {
  if (rotationTrack.length || !spin) return;
  const tracks = { ...(layer.tracks as Record<string, unknown>) };
  tracks.rotation = bakeContinuousSpin(spin, songEnd);
  layer.tracks = tracks;
}
