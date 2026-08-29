<script lang="ts">
  // Headless — owns playhead advancement + <audio> play/pause sync, mounted
  // exactly once in App.svelte regardless of which/how many canvas panels
  // (WorkspaceCanvas, PreviewCanvas) happen to be open in the dockview. Used
  // to live inside PreviewCanvas.svelte's own rAF loop, which worked when
  // there was only ever one canvas — now that Workspace and Preview are two
  // separate panels that can both be mounted at once (see the Capsule
  // viewport split), two independent copies of this loop would double-advance
  // the playhead. Both canvas components are pure renderers now: they read
  // `store.playhead`/`audio.currentLevel()` and draw, nothing more.
  import { onMount, onDestroy } from 'svelte';
  import type { ProjectStore } from '../state/project.svelte';
  import type { AudioState } from '../state/audio.svelte';

  let { store, audio }: { store: ProjectStore; audio: AudioState } = $props();

  let raf = 0;
  let lastFrameTime = 0;

  $effect(() => {
    if (!audio.loaded) return;
    if (store.playing) {
      audio.el.play().catch(() => {
        store.playing = false;
      });
    } else {
      audio.el.pause();
    }
  });

  function tick(now: number) {
    raf = requestAnimationFrame(tick);
    if (!lastFrameTime) lastFrameTime = now;
    const dt = (now - lastFrameTime) / 1000;
    lastFrameTime = now;
    if (!store.playing) return;

    if (audio.loaded) {
      store.playhead = audio.el.currentTime;
    } else {
      store.seekTo(store.playhead + dt);
    }

    const loop = store.loopEnabled ? store.loopRegion : null;
    if (loop && store.playhead >= loop.end) {
      store.playhead = loop.start;
      if (audio.loaded) audio.el.currentTime = loop.start;
    } else if (store.playhead >= store.project.meta.songEnd) {
      store.stop();
      audio.el.pause();
      audio.el.currentTime = 0;
    }
  }

  onMount(() => {
    raf = requestAnimationFrame(tick);
  });
  onDestroy(() => {
    cancelAnimationFrame(raf);
  });
</script>
