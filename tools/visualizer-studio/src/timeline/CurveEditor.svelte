<script lang="ts">
  import { onMount } from 'svelte';
  import { EASE_NAMES, sampleEase } from './curveEditor';
  import type { Ease } from '../types/project';

  let { value, onSelect }: { value: Ease; onSelect: (ease: Ease) => void } = $props();

  let canvases: Record<string, HTMLCanvasElement> = {};

  function drawThumb(canvas: HTMLCanvasElement, name: Ease, selected: boolean) {
    const ctx = canvas.getContext('2d')!;
    const W = canvas.width;
    const H = canvas.height;
    ctx.clearRect(0, 0, W, H);
    ctx.fillStyle = selected ? '#26262f' : '#1e1e27';
    ctx.fillRect(0, 0, W, H);
    const pts = sampleEase(name, 20);
    ctx.strokeStyle = selected ? '#ffd06e' : '#7fb7ff';
    ctx.lineWidth = 1.5;
    ctx.beginPath();
    pts.forEach((pt, i) => {
      const x = 4 + pt.x * (W - 8);
      const y = H - 4 - pt.y * (H - 8);
      if (i === 0) ctx.moveTo(x, y);
      else ctx.lineTo(x, y);
    });
    ctx.stroke();
  }

  $effect(() => {
    for (const name of EASE_NAMES) {
      const c = canvases[name];
      if (c) drawThumb(c, name, name === value);
    }
  });
</script>

<div class="curveEditor">
  {#each EASE_NAMES as name (name)}
    <button class="thumb" class:active={name === value} onclick={() => onSelect(name)} title={name}>
      <canvas bind:this={canvases[name]} width="48" height="32"></canvas>
      <span>{name}</span>
    </button>
  {/each}
</div>

<style>
  .curveEditor {
    display: flex;
    flex-wrap: wrap;
    gap: 6px;
    padding: 6px;
  }
  .thumb {
    display: flex;
    flex-direction: column;
    align-items: center;
    gap: 2px;
    padding: 3px;
    background: var(--bg2);
    border: 1px solid var(--line);
    border-radius: 4px;
  }
  .thumb.active {
    border-color: var(--accent2);
  }
  .thumb span {
    font: 9px var(--mono);
    color: var(--dim);
  }
  canvas {
    display: block;
  }
</style>
