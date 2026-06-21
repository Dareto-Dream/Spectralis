import type { Project } from '../types/project';
import { buildVisualizerHtml } from './buildVisualizerHtml';
import { buildModuleJson } from './buildModuleJson';
import { buildManifestJson } from './buildManifestJson';
import { buildReactiveJson } from './buildReactiveJson';
import { buildPackScript } from './buildPackScript';

export interface ExportContext {
  project: Project;
  audioSha256: string | null;
  coverExtension: string | null;
  sharedPlay: boolean;
}

export interface ExportFile {
  name: string;
  content: string;
}

export function buildExportFiles(ctx: ExportContext): ExportFile[] {
  const slug = ctx.project.meta.slug;
  return [
    { name: `${slug}_visualizer.html`, content: buildVisualizerHtml(ctx.project) },
    { name: `${slug}_module.json`, content: buildModuleJson(ctx.project) },
    { name: `${slug}_manifest.json`, content: buildManifestJson(ctx.project, ctx) },
    { name: `${slug}_reactive.json`, content: buildReactiveJson(ctx.project) },
    { name: `pack_${slug}.py`, content: buildPackScript(ctx.project) },
  ];
}
