<script lang="ts">
  import { onMount, onDestroy } from 'svelte';
  import type { ProjectStore } from '../state/project.svelte';
  import type { AudioState } from '../state/audio.svelte';
  import { drawPreviewFrame, initialFrameState, aspectSize } from './drawPreview';
  import { fmtTime } from '../lib/fmtTime';
  import { sectionAt } from '../core/render.js';

  // The forward-looking `readonly` prop this component used to carry is gone
  // — WorkspaceCanvas.svelte is the genuinely editable canvas now, and this
  // one was never given pointer handlers in the first place, so it was
  // always read-only in practice. Nothing ever passed `readonly` either way.
  let { store, audio }: { store: ProjectStore; audio: AudioState } = $props();

  let canvas: HTMLCanvasElement;
  let ctx: CanvasRenderingContext2D;
  let raf = 0;
  const frameState = initialFrameState();

  $effect(() => {
    const { w, h } = aspectSize(store.project.meta.aspect);
    if (canvas.width !== w) canvas.width = w;
    if (canvas.height !== h) canvas.height = h;
  });

  // Playhead advancement + <audio> play/pause sync now live in
  // TransportDriver.svelte (mounted once in App.svelte) — this is a pure
  // renderer, so it stays correct whether it's the only canvas mounted or
  // sitting alongside WorkspaceCanvas without double-driving transport.
  function tick(now: number) {
    raf = requestAnimationFrame(tick);
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
