import type { ProjectStore } from '../state/project.svelte';
import { copyKeyframes, pasteAtPlayhead, pasteAtOriginalTimes, hasClipboard } from '../timeline/clipboard';
import { closeContextMenu, contextMenuState } from '../timeline/contextMenu.svelte';
import { confirmModalState } from '../state/confirmModal.svelte';

const FRAME = 1 / 30;

function isTextInput(el: EventTarget | null): boolean {
  if (!(el instanceof HTMLElement)) return false;
  const tag = el.tagName;
  return tag === 'INPUT' || tag === 'TEXTAREA' || tag === 'SELECT' || el.isContentEditable;
}

// Single global handler, registered once in App.svelte. Early-returns while a
// text input has focus — the shortcuts here are all single-key or Ctrl-combo,
// so anything that would collide with normal typing gets skipped entirely.
export function createGlobalKeymap(store: ProjectStore, openHelp: () => void) {
  return function onKeyDown(e: KeyboardEvent) {
    // Modals/menus own Escape/Enter while they're open — don't double-handle.
    if (confirmModalState.request || contextMenuState.request) {
      if (e.key === 'Escape') closeContextMenu();
      return;
    }

    const typing = isTextInput(e.target);
    const mod = e.ctrlKey || e.metaKey;

    if (mod && e.key.toLowerCase() === 'z') {
      e.preventDefault();
      if (e.shiftKey) store.redo();
      else store.undo();
      return;
    }
    if (mod && e.key.toLowerCase() === 's') {
      e.preventDefault(); // prevents the browser's Save-page dialog
      document.querySelector<HTMLButtonElement>('.topbar button[data-action="save-project"]')?.click();
      return;
    }
    if (typing) return;

    if (e.key === ' ') {
      e.preventDefault();
      store.togglePlay();
    } else if (e.key === 'Delete' || e.key === 'Backspace') {
      if (store.selection.keyframeIds.size) {
        e.preventDefault();
        for (const id of [...store.selection.keyframeIds]) store.deleteKeyframe(id);
      } else if (store.selection.layerId) {
        e.preventDefault();
        store.deleteLayer(store.selection.layerId);
      }
    } else if (e.key === 'Escape') {
      store.selection.clearAll();
    } else if (mod && e.key.toLowerCase() === 'd') {
      if (store.selection.layerId) {
        e.preventDefault();
        store.duplicateLayer(store.selection.layerId);
      }
    } else if (mod && e.key.toLowerCase() === 'c') {
      const entries = [...store.selection.keyframeIds]
        .map((id) => store.selection.keyframeIndex.get(id))
        .filter((ref): ref is NonNullable<typeof ref> => !!ref)
        .map((ref) => ({ layerId: ref.layer.id, trackKey: ref.trackKey, ...ref.layer.tracks[ref.trackKey][ref.index] }));
      if (entries.length) copyKeyframes(entries, store.playhead);
    } else if (mod && e.key.toLowerCase() === 'v' && hasClipboard()) {
      e.preventDefault();
      store.pasteKeyframes(e.shiftKey ? pasteAtOriginalTimes() : pasteAtPlayhead(store.playhead));
    } else if (e.key === 'ArrowLeft' || e.key === 'ArrowRight') {
      e.preventDefault();
      const delta = (e.key === 'ArrowLeft' ? -1 : 1) * (e.shiftKey ? 1 : FRAME);
      if (store.selection.keyframeIds.size) {
        for (const id of store.selection.keyframeIds) {
          const ref = store.selection.keyframeIndex.get(id);
          if (ref) store.moveKeyframeTime(id, ref.layer.tracks[ref.trackKey][ref.index].t + delta);
        }
        store.commit();
      } else {
        store.seekTo(store.playhead + delta);
      }
    } else if (e.key === '?' || (e.shiftKey && e.key === '/')) {
      e.preventDefault();
      openHelp();
    }
  };
}
