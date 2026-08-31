// Wraps AudioState.loadFile so every call site also syncs
// project.meta.songEnd to the real audio duration — song end is DERIVED from
// the loaded track now, not a number you have to type/guess and keep in
// sync yourself. `el.duration` isn't known synchronously right after setting
// `el.src`, so this waits for 'loadedmetadata' when it isn't already
// available (e.g. a fast local file often already has it by the time
// loadFile's own awaits finish).
import type { ProjectStore } from '../state/project.svelte';
import type { AudioState } from '../state/audio.svelte';

function waitForDuration(audio: AudioState): Promise<number> {
  const existing = audio.el.duration;
  if (Number.isFinite(existing) && existing > 0) return Promise.resolve(existing);
  return new Promise((resolve) => {
    const onReady = () => {
      audio.el.removeEventListener('loadedmetadata', onReady);
      resolve(audio.el.duration);
    };
    audio.el.addEventListener('loadedmetadata', onReady, { once: true });
  });
}

export async function loadAudioFile(store: ProjectStore, audio: AudioState, file: File): Promise<void> {
  await audio.loadFile(file);
  if (!audio.loaded) return; // loadFile already surfaced its own toast/error state
  const duration = await waitForDuration(audio);
  if (Number.isFinite(duration) && duration > 0) {
    store.updateMeta({ songEnd: duration });
  }
}
