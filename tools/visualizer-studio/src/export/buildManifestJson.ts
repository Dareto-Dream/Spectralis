import type { Project } from '../types/project';
import { deriveCapabilities } from './capabilities';
import { buildReactiveEvents } from './buildReactiveJson';

export interface ManifestOptions {
  // Gap 1: null means audio hasn't been loaded yet — never a silent placeholder,
  // the manifest gets a visibly-labeled sentinel and the export UI warns.
  audioSha256: string | null;
  // Gap 2: null means no cover attached (identical to today's already-correct
  // empty-state behavior); otherwise the file extension to name the asset with.
  coverExtension: string | null;
  sharedPlay: boolean;
}

export function buildManifestJson(project: Project, opts: ManifestOptions): string {
  const slug = project.meta.slug;
  const hasLyrics = project.layers.some((l) => l.type === 'lyrics');
  const reactiveEventCount = buildReactiveEvents(project).length;

  const images = opts.coverExtension ? [`assets/images/${slug}_cover.${opts.coverExtension}`] : [];
  // `binaryAssets` was entirely missing from the old exporter — the exported
  // HTML already hardcodes `<img src="delta-asset:cover">` expecting exactly
  // this binding, so a cover was attached but never actually reachable.
  const binaryAssets: Record<string, string> = opts.coverExtension
    ? { cover: `assets/images/${slug}_cover.${opts.coverExtension}` }
    : {};

  return JSON.stringify(
    {
      format: 'spectralis-capsule',
      formatVersion: 3,
      id: `${slug}-${new Date().toISOString().slice(0, 10)}`,
      title: project.meta.title,
      artist: project.meta.artist || 'Unknown',
      release: { year: new Date().getFullYear(), credits: [] },
      audio: {
        entry: `audio/${slug}.wav`,
        sha256: opts.audioSha256 ?? 'PENDING-LOAD-AUDIO-TO-COMPUTE',
        durationSeconds: project.meta.songEnd,
      },
      assets: { images, fonts: [], videos: [], data: hasLyrics ? [`assets/data/${slug}.lrc`] : [] },
      visualizers: [
        {
          id: `${slug}_studio`,
          type: 'html',
          runtime: 'html',
          moduleEntry: `assets/data/${slug}_module.json`,
          binaryEntry: `assets/html/${slug}_visualizer.html`,
          dataAssets: hasLyrics ? { lyrics: `assets/data/${slug}.lrc` } : {},
          binaryAssets,
        },
      ],
      timeline: [{ entry: 'reactive.json', type: 'spectralis-track-reactive', version: 3 }],
      suppressAppLyrics: hasLyrics,
      capabilities: deriveCapabilities(project, reactiveEventCount, { sharedPlay: opts.sharedPlay }),
      signature: { keyId: 'FILL-IN-YOUR-KEY-ID', fingerprint: 'FILL-IN-VIA-PACK-SCRIPT', algorithm: 'Ed25519', value: 'capsule-header' },
    },
    null,
    2
  );
}
