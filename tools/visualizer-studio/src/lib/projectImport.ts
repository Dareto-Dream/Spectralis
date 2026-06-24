// Shared by the top bar's "Load Project" file input AND the top bar's own
// drop zone (plan QoL §C/§D) — both routes go through the same unsaved-
// changes gate, migrate step, and parse-error dialog.
import type { ProjectStore } from '../state/project.svelte';
import { confirmDialog } from '../state/confirmModal.svelte';
import { toast } from '../state/toast.svelte';
import { migrateProject } from './migrate';

export async function confirmUnsavedIfNeeded(store: ProjectStore): Promise<boolean> {
  if (!store.history.hasUncommittedSinceLoad) return true;
  return confirmDialog({
    title: 'Discard unsaved changes?',
    body: 'Loading will replace the current project. This can still be undone with Ctrl+Z right up until you load something else.',
    confirmLabel: 'Discard & Load',
    danger: true,
  });
}

export async function importProjectFile(store: ProjectStore, file: File): Promise<void> {
  if (!(await confirmUnsavedIfNeeded(store))) return;
  try {
    const parsed = migrateProject(JSON.parse(await file.text()));
    store.loadProject(parsed);
    toast.push('success', `Loaded ${file.name}`);
  } catch (err) {
    await confirmDialog({
      title: "Couldn't load that project",
      body: `This file isn't a valid Studio project — ${err instanceof Error ? err.message : String(err)}`,
      confirmLabel: 'OK',
    });
  }
}
