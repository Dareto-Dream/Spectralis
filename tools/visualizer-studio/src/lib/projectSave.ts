// Extracted out of TopBar.svelte so both the menu bar (File > Save Project)
// and the Ctrl+S shortcut can call the same function directly — the shortcut
// used to reach this by querying a specific DOM button (`.topbar
// button[data-action="save-project"]`), which broke the moment that button
// moved into a menu. A real function reference doesn't care where the UI lives.
//
// Saves as a real .spectralis capsule file (see src/format/capsuleFile.ts)
// instead of the old flat `.studio.json` — audio/cover source paths ride
// along in the PATH chunk, everything else (layers/sections/tracks) is
// unchanged, just wrapped as the VIZP payload.
import type { ProjectStore } from '../state/project.svelte';
import type { AudioState } from '../state/audio.svelte';
import type { AssetsState } from '../state/assets.svelte';
import { downloadBytes } from './downloadText';
import { toast } from '../state/toast.svelte';
import { autosave } from '../state/autosave.svelte';
import { encodeCapsuleFile, newCapsuleMeta } from '../format/capsuleFile';

export async function saveProjectFile(store: ProjectStore, audio: AudioState, assets: AssetsState) {
  const slug = store.project.meta.slug || 'project';
  const bytes = encodeCapsuleFile({
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
  });

  if (window.native) {
    const root = await window.native.getStudioRoot();
    const chosen = await window.native.saveFileDialog({
      defaultPath: `${root}/Projects/${slug}.spectralis`,
      filters: [{ name: 'Spectralis Capsule', extensions: ['spectralis'] }],
    });
    if (!chosen) return; // user cancelled the save dialog
    // Same ArrayBuffer-vs-ArrayBufferLike type-system gap as downloadBytes —
    // `bytes` is always a freshly allocated, non-shared buffer.
    await window.native.writeBinaryFile(chosen, bytes.buffer.slice(bytes.byteOffset, bytes.byteOffset + bytes.byteLength) as ArrayBuffer);
  } else {
    downloadBytes(`${slug}.spectralis`, bytes);
  }
  autosave.clear();
  toast.push('success', 'Project saved');
}
