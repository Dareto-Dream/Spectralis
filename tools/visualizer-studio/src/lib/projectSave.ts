// Extracted out of TopBar.svelte so both the menu bar (File > Save Project)
// and the Ctrl+S shortcut can call the same function directly — the shortcut
// used to reach this by querying a specific DOM button (`.topbar
// button[data-action="save-project"]`), which broke the moment that button
// moved into a menu. A real function reference doesn't care where the UI lives.
import type { ProjectStore } from '../state/project.svelte';
import { downloadText } from './downloadText';
import { toast } from '../state/toast.svelte';
import { autosave } from '../state/autosave.svelte';

export function saveProjectFile(store: ProjectStore) {
  downloadText(`${store.project.meta.slug || 'project'}.studio.json`, JSON.stringify(store.project, null, 2));
  autosave.clear();
  toast.push('success', 'Project saved');
}
