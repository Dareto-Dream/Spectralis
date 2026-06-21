import type { Project } from '../types/project';

export interface CapabilityOptions {
  // sharedPlay.* varies per-project in ways content alone can't predict, and
  // getting it wrong has real consequences in the CDN key-trust flow — manual
  // opt-in only, never inferred. Old exporter always included both, unconditionally.
  sharedPlay: boolean;
}

// Derives the capability list from actual project content instead of the old
// tool's static hardcoded array (which always claimed sharedPlay.* regardless
// of whether the project used it at all).
export function deriveCapabilities(project: Project, reactiveEventCount: number, opts: CapabilityOptions): string[] {
  const caps = ['webview.localContent'];
  if (project.layers.length > 1) caps.push('visualizer.multiLayer');
  if (reactiveEventCount > 0) caps.push('timeline.appControl', 'app.theme.deepControl');
  if (opts.sharedPlay) caps.push('sharedPlay.hostCapsule', 'sharedPlay.packageUpload');
  return caps;
}
