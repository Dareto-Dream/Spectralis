// Extracted out of TopBar.svelte so both the menu bar (File > Save Project)
// and the Ctrl+S shortcut can call the same function directly — the shortcut
// used to reach this by querying a specific DOM button (`.topbar
// button[data-action="save-project"]`), which broke the moment that button
// moved into a menu. A real function reference doesn't care where the UI lives.
//
// Saves as a real .spex capsule file (see src/format/capsuleFile.ts, magic
// `SPEX`) instead of the old flat `.studio.json` — audio/cover source paths
// ride along in the PATH chunk, everything else (layers/sections/tracks) is
// unchanged, just wrapped as the VIZP payload. NOT .spectralis — that
// extension is reserved for the real signed SPCC-v3 capsule the pack script
// produces from an EXPORT (see export/buildPackScript.ts); this is Visualizer
// Studio's own working project file, a completely different artifact that
// used to confusingly share the same extension.
import type { ProjectStore } from '../state/project.svelte';
import type { AudioState } from '../state/audio.svelte';
import type { AssetsState } from '../state/assets.svelte';
import { downloadBytes } from './downloadText';
import { toast } from '../state/toast.svelte';
import { capsuleAutosave } from '../state/autosave.svelte';
import { encodeCapsuleFile, newCapsuleMeta, type CapsuleFile } from '../format/capsuleFile';

// Shared with state/autosave.svelte.ts's capsule autosave — one place builds
// "what a CapsuleFile looks like right now" so the manual Save path and the
// autosave safety net can never drift into two different shapes.
export function buildCapsuleFile(store: ProjectStore, audio: AudioState, assets: AssetsState): CapsuleFile {
  return {
    meta: newCapsuleMeta({
      title: store.project.meta.title,
      artist: store.project.meta.artist,
      duration: store.project.meta.songEnd,
    }),
    paths: {
      audio: audio.filePath ?? '',
      cover: assets.coverImage?.path ?? '',
    },
    project: store.project,
    passthrough: [],
  };
}

// Shared core for both Save (Ctrl+S) and Save As (Ctrl+Shift+S) — `forcePrompt`
// is the only difference: Save reuses store.knownFilePath silently once one
// exists (from a prior save, or from opening a real file), Save As always
// asks and adopts whatever gets picked as the new known path. Neither
// distinction exists in the browser build — there's no real filesystem to
// remember a path into, so every save is just another download there.
async function writeProjectFile(store: ProjectStore, audio: AudioState, assets: AssetsState, forcePrompt: boolean) {
  const slug = store.project.meta.slug || 'project';
  const bytes = encodeCapsuleFile(buildCapsuleFile(store, audio, assets));

  if (window.native) {
    let path = forcePrompt ? null : store.knownFilePath;
    if (!path) {
      const root = await window.native.getStudioRoot();
      const chosen = await window.native.saveFileDialog({
        defaultPath: store.knownFilePath ?? `${root}/Projects/${slug}.spex`,
        filters: [{ name: 'Spectralis Studio Project', extensions: ['spex'] }],
      });
      if (!chosen) return; // user cancelled the save dialog
      path = chosen;
    }
    // Same ArrayBuffer-vs-ArrayBufferLike type-system gap as downloadBytes —
    // `bytes` is always a freshly allocated, non-shared buffer.
    await window.native.writeBinaryFile(path, bytes.buffer.slice(bytes.byteOffset, bytes.byteOffset + bytes.byteLength) as ArrayBuffer);
    store.knownFilePath = path;
  } else {
    downloadBytes(`${slug}.spex`, bytes);
  }
  void capsuleAutosave.clear();
  store.history.markSaved();
  toast.push('success', 'Project saved');
}

export function saveProjectFile(store: ProjectStore, audio: AudioState, assets: AssetsState) {
  return writeProjectFile(store, audio, assets, false);
}

export function saveProjectFileAs(store: ProjectStore, audio: AudioState, assets: AssetsState) {
  return writeProjectFile(store, audio, assets, true);
}
