// Export pre-flight + orchestration, extracted out of TopBar.svelte so Tools >
// Render Capsule in the menu bar can trigger it without TopBar in the tree —
// the top bar is now just the project-properties strip, not where actions live.
import type { ProjectStore } from '../state/project.svelte';
import type { AudioState } from '../state/audio.svelte';
import type { AssetsState } from '../state/assets.svelte';
import { confirmDialog } from '../state/confirmModal.svelte';
import { toast } from '../state/toast.svelte';
import { downloadText } from './downloadText';
import { buildExportFiles } from '../export/exportAll';
import { exportSettings } from '../state/exportSettings.svelte';

// QoL export pre-flight: slug is a hard requirement (nothing to name the files
// with); zero layers / no audio are warn-level confirms, not hard blocks — a
// silent visualizer with no audio, or an export with no layers yet, are both
// legal if unusual, so they get a nudge instead of being refused outright.
async function preflightOk(store: ProjectStore, audio: AudioState): Promise<boolean> {
  if (!store.project.meta.slug) {
    toast.push('error', 'Set a slug in the project properties first.');
    return false;
  }
  if (store.project.layers.length === 0) {
    const ok = await confirmDialog({
      title: 'Export with no layers?',
      body: 'This project has no layers yet — the exported visualizer will render nothing but the section background.',
      confirmLabel: 'Export anyway',
    });
    if (!ok) return false;
  }
  if (!audio.loaded) {
    const ok = await confirmDialog({
      title: 'Export without audio?',
      body: "No audio is loaded — the manifest's audio.sha256 will be a placeholder until you load a track and re-export.",
      confirmLabel: 'Export anyway',
    });
    if (!ok) return false;
  }
  return true;
}

export async function renderCapsule(store: ProjectStore, audio: AudioState, assets: AssetsState) {
  if (exportSettings.exporting) return;
  if (!(await preflightOk(store, audio))) return;
  exportSettings.exporting = true;
  try {
    // buildVisualizerHtml et al are plain (non-Svelte) modules so they stay
    // testable without the runes machinery — structuredClone inside them can't
    // clone a live $state proxy directly, so snapshot to a plain object first.
    const project = $state.snapshot(store.project) as typeof store.project;
    const files = buildExportFiles({
      project,
      audioSha256: audio.sha256,
      coverExtension: assets.coverImage ? assets.extension : null,
      sharedPlay: exportSettings.sharedPlay,
    });
    for (const file of files) downloadText(file.name, file.content);
    toast.push('success', `Exported ${files.length} files for ${store.project.meta.slug}`);
  } finally {
    exportSettings.exporting = false;
  }
}
