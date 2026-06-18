<script lang="ts">
  import { onMount, onDestroy } from 'svelte';
  import type { ProjectStore } from '../state/project.svelte';
  import type { AudioState } from '../state/audio.svelte';
  import { drawPreviewFrame, initialFrameState, aspectSize } from './drawPreview';
  import { fmtTime } from '../lib/fmtTime';
  import { sectionAt } from '../core/render.js';

  let { store, audio }: { store: ProjectStore; audio: AudioState } = $props();

  let canvas: HTMLCanvasElement;
  let ctx: CanvasRenderingContext2D;
  let raf = 0;
  const frameState = initialFrameState();
  let lastFrameTime = 0;

  $effect(() => {
    const { w, h } = aspectSize(store.project.meta.aspect);
    if (canvas.width !== w) canvas.width = w;
    if (canvas.height !== h) canvas.height = h;
  });

  // Keeps the real <audio> element's transport in lockstep with the store's
  // `playing` flag — the RAF loop below only ever READS audio.el.currentTime.
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

    if (store.playing) {
      if (audio.loaded) {
        store.playhead = audio.el.currentTime;
      } else {
        store.seekTo(store.playhead + dt);
      }
      if (store.playhead >= store.project.meta.songEnd) {
        store.stop();
        audio.el.pause();
        audio.el.currentTime = 0;
      }
    }

    const level = audio.loaded ? audio.currentLevel() : { peak: 0, rms: 0 };
    drawPreviewFrame(ctx, canvas.width, canvas.height, store.project, store.playhead, now, level, frameState, store.soloedLayerIds);
  }

  onMount(() => {
    ctx = canvas.getContext('2d')!;
    raf = requestAnimationFrame(tick);
  });

  onDestroy(() => {
    cancelAnimationFrame(raf);
  });

  let overlayText = $derived(
    `${fmtTime(store.playhead)}  ·  section: ${sectionAt(store.project.sections, store.playhead)?.label ?? '—'}${store.playing ? '  ▶' : ''}`
  );
</script>

<div class="previewCanvasWrap">
  <canvas bind:this={canvas}></canvas>
  <div class="previewOverlay">{overlayText}</div>
</div>

<style>
  .previewCanvasWrap {
    position: relative;
    display: inline-block;
    background: #000;
  }
  canvas {
    display: block;
  }
  .previewOverlay {
    position: absolute;
    top: 6px;
    left: 8px;
    font: 11px var(--mono);
    color: var(--dim);
    pointer-events: none;
    text-shadow: 0 1px 2px rgba(0, 0, 0, 0.8);
  }
</style>
