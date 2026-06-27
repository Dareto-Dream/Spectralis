import type { ProjectStore } from '../state/project.svelte';

// Shared by the Delete/Backspace shortcut (lib/keymap.ts) and the Edit >
// Delete Selection menu item — keyframe selection takes priority over layer
// selection, matching the app's existing context-sensitive delete behavior.
export function deleteSelection(store: ProjectStore) {
  if (store.selection.keyframeIds.size) {
    for (const id of [...store.selection.keyframeIds]) store.deleteKeyframe(id);
  } else if (store.selection.layerId) {
    store.deleteLayer(store.selection.layerId);
  }
}
