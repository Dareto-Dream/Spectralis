// Shared by the top bar's "Load Project" file input AND the top bar's own
// drop zone (plan QoL §C/§D) — both routes go through the same unsaved-
// changes gate, migrate step, and parse-error dialog.
import type { ProjectStore } from '../state/project.svelte';
import { confirmDialog } from '../state/confirmModal.svelte';
import { toast } from '../state/toast.svelte';
import { migrateProject } from './migrate';
import { decodeCapsuleFile } from '../format/capsuleFile';
import type { Template } from './templates';

export async function confirmUnsavedIfNeeded(store: ProjectStore): Promise<boolean> {
  if (!store.history.hasUncommittedSinceLoad) return true;
  return confirmDialog({
    title: 'Discard unsaved changes?',
    body: 'Loading will replace the current project. This can still be undone with Ctrl+Z right up until you load something else.',
    confirmLabel: 'Discard & Load',
    danger: true,
  });
}

// Shared by MenuBar's File > Load Template submenu and the Assets panel's
// Templates category — a "template" here is a full starter project (see
// lib/templates.ts), gated behind the same discard-unsaved-work confirm as
// every other "replace the current project" action.
export async function loadTemplateIntoStore(store: ProjectStore, tpl: Template): Promise<boolean> {
  if (!(await confirmUnsavedIfNeeded(store))) return false;
  store.loadProject(tpl.build());
  toast.push('success', `Loaded "${tpl.label}"`);
  return true;
}

// Reads either a real .spectralis capsule file (sniffed by its "SPEX" magic)
// or a legacy flat `.studio.json` save — both keep working indefinitely, the
// binary format is purely additive.
export async function importProjectFile(store: ProjectStore, file: File): Promise<void> {
  if (!(await confirmUnsavedIfNeeded(store))) return;
  try {
    const bytes = new Uint8Array(await file.arrayBuffer());
    const magic = new TextDecoder('ascii').decode(bytes.subarray(0, 4));
    const project = magic === 'SPEX' || magic === 'SPWX' ? decodeCapsuleFile(bytes).project : migrateProject(JSON.parse(new TextDecoder('utf-8').decode(bytes)));
    store.loadProject(project);
    toast.push('success', `Loaded ${file.name}`);
  } catch (err) {
    await confirmDialog({
      title: "Couldn't load that project",
      body: `This file isn't a valid Studio project — ${err instanceof Error ? err.message : String(err)}`,
      confirmLabel: 'OK',
    });
  }
}
