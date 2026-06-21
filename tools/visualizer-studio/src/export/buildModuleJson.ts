import type { Project } from '../types/project';

export function buildModuleJson(project: Project): string {
  const slug = project.meta.slug;
  return JSON.stringify(
    {
      id: `${slug}_studio`,
      type: 'html',
      runtime: 'html',
      entry: '',
      version: '1.0.0',
      binaryRef: `${slug}_html`,
      width: 1920,
      height: 1080,
      dataRefs: { cover: `${slug}_cover`, lyrics: `${slug}_lrc` },
    },
    null,
    2
  );
}
