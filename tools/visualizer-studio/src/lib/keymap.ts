import type { ProjectStore } from '../state/project.svelte';
import type { AudioState } from '../state/audio.svelte';
import type { AssetsState } from '../state/assets.svelte';
import { copyKeyframes, pasteAtPlayhead, pasteAtOriginalTimes, hasClipboard } from '../timeline/clipboard';
import { closeContextMenu, contextMenuState } from '../timeline/contextMenu.svelte';
import { confirmModalState } from '../state/confirmModal.svelte';
import { saveProjectFile, saveProjectFileAs } from './projectSave';
import { saveWorldFile, saveWorldFileAs } from './worldSave';
import { appMode } from '../state/appMode.svelte';
import { deleteSelection } from './deleteSelection';

const FRAME = 1 / 30;

export type ShortcutCategory = 'Playback' | 'Editing' | 'Selection' | 'View' | 'General';

export interface ShortcutEntry {
  keys: string;
  description: string;
  category: ShortcutCategory;
}

// Single source of truth for the Help modal — kept in this file so the list
// can't drift from createGlobalKeymap below without someone noticing the diff.
export const SHORTCUTS: ShortcutEntry[] = [
  { keys: 'Space', description: 'Play / Pause', category: 'Playback' },
  { keys: '← / →', description: 'Nudge playhead (or selected keyframes) by 1 frame', category: 'Playback' },
  { keys: 'Shift+← / Shift+→', description: 'Nudge by 1 second', category: 'Playback' },
  { keys: '[ / ]', description: 'Set selected section start/end to playhead', category: 'Playback' },
  { keys: 'Ctrl+Z / Ctrl+Shift+Z', description: 'Undo / Redo', category: 'Editing' },
  { keys: 'Delete / Backspace', description: 'Delete selected keyframe(s) or layer', category: 'Editing' },
  { keys: 'Ctrl+D', description: 'Duplicate selected layer', category: 'Editing' },
  { keys: 'K', description: 'Add a keyframe at the playhead for the selected property', category: 'Editing' },
  { keys: 'Ctrl+C / Ctrl+V', description: 'Copy / Paste keyframes at playhead', category: 'Editing' },
  { keys: 'Ctrl+Shift+V', description: 'Paste keyframes at original times', category: 'Editing' },
  { keys: 'F2', description: 'Rename selected layer', category: 'Editing' },
  { keys: 'Ctrl+A', description: 'Select all keyframes in the active track', category: 'Selection' },
  { keys: '↑ / ↓', description: 'Move layer selection up/down the list', category: 'Selection' },
  { keys: 'Escape', description: 'Clear selection', category: 'Selection' },
  { keys: '+ / -', description: 'Timeline zoom in/out', category: 'View' },
  { keys: 'Double-click a lane', description: 'Add a keyframe at that time', category: 'View' },
  { keys: 'Scroll wheel over timeline', description: 'Zoom, anchored at cursor', category: 'View' },
  { keys: 'Right-click keyframe/section/layer', description: 'Context menu', category: 'View' },
  { keys: 'Ctrl+S', description: 'Save (Capsule or World, whichever mode you\'re in)', category: 'General' },
  { keys: 'Ctrl+Shift+S', description: 'Save As — always prompts for a new location', category: 'General' },
  { keys: '?', description: 'Show this help', category: 'General' },
];

// Exported for anything else outside the global keymap that needs to skip
// text-input focus the same way (e.g. WorkspaceCanvas's Space-to-pan, which
// would otherwise eat literal spacebar keystrokes typed into a field).
export function isTextInput(el: EventTarget | null): boolean {
  if (!(el instanceof HTMLElement)) return false;
  const tag = el.tagName;
  return tag === 'INPUT' || tag === 'TEXTAREA' || tag === 'SELECT' || el.isContentEditable;
}

// Single global handler, registered once in App.svelte. Early-returns while a
// text input has focus — the shortcuts here are all single-key or Ctrl-combo,
// so anything that would collide with normal typing gets skipped entirely.
export function createGlobalKeymap(store: ProjectStore, audio: AudioState, assets: AssetsState, openHelp: () => void) {
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
      // Mode-aware — this used to always save the Capsule project even while
      // in World mode, which would silently save/prompt for the WRONG thing
      // (and never touch the World graph you were actually looking at) if
      // you hit Ctrl+S there instead of using the menu's own Save World.
      if (appMode.mode === 'world') {
        if (e.shiftKey) saveWorldFileAs();
        else saveWorldFile();
      } else if (e.shiftKey) {
        saveProjectFileAs(store, audio, assets);
      } else {
        saveProjectFile(store, audio, assets);
      }
      return;
    }
    if (typing) return;

    if (e.key === ' ') {
      e.preventDefault();
      store.togglePlay();
    } else if (e.key === 'Delete' || e.key === 'Backspace') {
      if (store.selection.keyframeIds.size || store.selection.layerId) {
        e.preventDefault();
        deleteSelection(store);
      }
    } else if (e.key === 'Escape') {
      store.selection.clearAll();
    } else if (mod && e.key.toLowerCase() === 'd') {
      if (store.selection.layerId) {
        e.preventDefault();
        store.duplicateLayer(store.selection.layerId);
      }
    } else if (e.key.toLowerCase() === 'k') {
      // Same action as PropRow.svelte's "+Key" button (or ShapeAnimRow's, for
      // a selected shape's own motion) — a keyboard shortcut for whatever
      // property is currently selected, rather than requiring a mouse trip
      // to the Inspector every time.
      if (store.selection.layerId && store.selection.trackKey) {
        e.preventDefault();
        store.addKeyframeAtPlayhead(store.selection.layerId, store.selection.trackKey);
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
    } else if (e.key === 'ArrowUp' || e.key === 'ArrowDown') {
      e.preventDefault();
      store.selectAdjacentLayer(e.key === 'ArrowUp' ? -1 : 1);
    } else if (mod && e.key.toLowerCase() === 'a') {
      // Select-all is scoped to the active track (not a global "select
      // everything") so it stays useful once multiple layers are keyframed.
      if (store.selection.layerId && store.selection.trackKey) {
        e.preventDefault();
        const layer = store.project.layers.find((l) => l.id === store.selection.layerId);
        if (layer) store.selection.setKeyframeSelection(layer.tracks[store.selection.trackKey].map((k) => k.id));
      }
    } else if (e.key === '[' || e.key === ']') {
      // Set the selected section's start/end to the playhead — remapped from
      // AE's B/N since those aren't intuitive without an existing convention here.
      if (store.selection.sectionId) {
        e.preventDefault();
        store.updateSection(store.selection.sectionId, e.key === '[' ? { start: store.playhead } : { end: store.playhead });
      }
    } else if (e.key === '+' || e.key === '=' || e.key === '-' || e.key === '_') {
      e.preventDefault();
      store.zoomTimeline(e.key === '+' || e.key === '=' ? 1 : -1);
    } else if (e.key === 'F2') {
      if (store.selection.layerId) {
        e.preventDefault();
        store.selection.requestRename();
      }
    } else if (e.key === '?' || (e.shiftKey && e.key === '/')) {
      e.preventDefault();
      openHelp();
    }
  };
}
