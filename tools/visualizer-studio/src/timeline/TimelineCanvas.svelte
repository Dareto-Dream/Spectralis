<script lang="ts">
  import type { ProjectStore } from '../state/project.svelte';
  import type { AudioState } from '../state/audio.svelte';
  import { computeLayout } from './layout';
  import { drawTimeline } from './draw';
  import {
    hitTestActiveLaneKeyframe,
    hitTestOverviewRow,
    hitTestSection,
    hitTestSectionEdge,
    hitTestScrub,
    keyframesInMarquee,
    type MarqueeRect,
    type SectionEdge,
  } from './interactions';
  import { snap, snapTargets } from './snapping';
  import { copyKeyframes, pasteAtPlayhead, pasteAtOriginalTimes, hasClipboard } from './clipboard';
  import type { AnimKey } from '../types/project';
  import { onMount } from 'svelte';

  let { store, audio }: { store: ProjectStore; audio: AudioState } = $props();

  let canvas: HTMLCanvasElement;
  let ctx: CanvasRenderingContext2D;
  let ready = $state(false);

  onMount(() => {
    ctx = canvas.getContext('2d')!;
    ready = true;
  });
  let pxPerSec = $state(80);
  let snapEnabled = $state(true);

  type Drag =
    | { kind: 'keyframe'; anchorT: number; originalTimes: Map<string, number> }
    | { kind: 'sectionEdge'; sectionId: string; edge: SectionEdge }
    | { kind: 'scrub' }
    | { kind: 'marquee'; rect: MarqueeRect }
    | null;
  let drag: Drag = null;

  function localPoint(e: MouseEvent): { x: number; y: number } {
    const rect = canvas.getBoundingClientRect();
    return { x: e.clientX - rect.left, y: e.clientY - rect.top };
  }

  function currentLayout() {
    return computeLayout(store.project.layers.length);
  }

  $effect(() => {
    if (!ready) return;
    const layout = computeLayout(store.project.layers.length);
    const w = Math.max(canvas.clientWidth || 0, store.project.meta.songEnd * pxPerSec + 60);
    if (canvas.width !== Math.ceil(w)) canvas.width = Math.ceil(w);
    if (canvas.height !== layout.totalH) canvas.height = layout.totalH;

    drawTimeline({
      ctx,
      W: canvas.width,
      H: canvas.height,
      layout,
      project: store.project,
      playhead: store.playhead,
      pxPerSec,
      scrollX: 0,
      selection: {
        layerId: store.selection.layerId,
        trackKey: store.selection.trackKey,
        keyframeIds: store.selection.keyframeIds,
        sectionId: store.selection.sectionId,
      },
      waveformPeaks: audio.waveformPeaks,
    });
  });

  function onPointerDown(e: MouseEvent) {
    canvas.focus();
    const { x, y } = localPoint(e);
    const layout = currentLayout();
    const t = x / pxPerSec;

    const edgeHit = hitTestSectionEdge(layout, store.project, pxPerSec, x, y);
    if (edgeHit) {
      store.selection.selectSection(edgeHit.sectionId);
      drag = { kind: 'sectionEdge', sectionId: edgeHit.sectionId, edge: edgeHit.edge };
      return;
    }

    const kfHit = hitTestActiveLaneKeyframe(layout, store.project, store.selection.layerId, store.selection.trackKey, pxPerSec, x, y);
    if (kfHit) {
      const additive = e.shiftKey || e.ctrlKey || e.metaKey;
      if (!additive && !store.selection.keyframeIds.has(kfHit.keyframeId)) {
        store.selection.setKeyframeSelection([kfHit.keyframeId]);
      } else if (additive) {
        store.selection.selectKeyframe(kfHit.keyframeId, { additive: true });
      }
      const originalTimes = new Map<string, number>();
      for (const id of store.selection.keyframeIds) {
        const ref = store.selection.keyframeIndex.get(id);
        if (ref) originalTimes.set(id, ref.layer.tracks[ref.trackKey][ref.index].t);
      }
      drag = { kind: 'keyframe', anchorT: t, originalTimes };
      return;
    }

    const secHit = hitTestSection(layout, store.project, pxPerSec, x, y);
    if (secHit) {
      store.selection.selectSection(secHit);
      return;
    }

    if (hitTestScrub(layout, y)) {
      store.seekTo(t);
      drag = { kind: 'scrub' };
      return;
    }

    const rowHit = hitTestOverviewRow(layout, store.project, y);
    if (rowHit) {
      store.selection.selectLayer(rowHit.id);
      return;
    }

    if (store.selection.layerId && store.selection.trackKey) {
      drag = { kind: 'marquee', rect: { x0: x, y0: y, x1: x, y1: y } };
      store.selection.clearKeyframes();
    }
  }

  function onPointerMove(e: MouseEvent) {
    if (!drag) return;
    const { x, y } = localPoint(e);
    const t = Math.max(0, x / pxPerSec);

    if (drag.kind === 'keyframe') {
      const dt = t - drag.anchorT;
      const targets = snapEnabled ? snapTargets(store.project, store.playhead, new Set(drag.originalTimes.keys())) : [];
      for (const [id, origT] of drag.originalTimes) {
        const raw = Math.max(0, origT + dt);
        const finalT = snapEnabled ? snap(raw, targets, pxPerSec) : raw;
        store.moveKeyframeTime(id, finalT);
      }
    } else if (drag.kind === 'sectionEdge') {
      store.updateSectionLive(drag.sectionId, drag.edge === 'start' ? { start: t } : { end: t });
    } else if (drag.kind === 'scrub') {
      store.seekTo(t);
    } else if (drag.kind === 'marquee') {
      drag.rect = { ...drag.rect, x1: x, y1: y };
      const ids = keyframesInMarquee(store.project, store.selection.layerId, store.selection.trackKey, pxPerSec, drag.rect);
      store.selection.setKeyframeSelection(ids);
    }
  }

  function onPointerUp() {
    if (drag?.kind === 'keyframe' || drag?.kind === 'sectionEdge') {
      store.commit();
    }
    drag = null;
  }

  function onDblClick(e: MouseEvent) {
    const { x, y } = localPoint(e);
    const layout = currentLayout();
    if (layout.laneY <= y && y < layout.totalH && store.selection.layerId && store.selection.trackKey) {
      store.addKeyframeAt(store.selection.layerId, store.selection.trackKey, Math.max(0, x / pxPerSec));
    }
  }

  function selectedKeyframeEntries() {
    const entries: { layerId: string; trackKey: AnimKey; t: number; v: number; ease: any }[] = [];
    for (const id of store.selection.keyframeIds) {
      const ref = store.selection.keyframeIndex.get(id);
      if (!ref) continue;
      const kf = ref.layer.tracks[ref.trackKey][ref.index];
      entries.push({ layerId: ref.layer.id, trackKey: ref.trackKey, t: kf.t, v: kf.v, ease: kf.ease });
    }
    return entries;
  }

  function onKeyDown(e: KeyboardEvent) {
    if (e.key === 'Delete' || e.key === 'Backspace') {
      e.preventDefault();
      for (const id of [...store.selection.keyframeIds]) store.deleteKeyframe(id);
    } else if (e.key === 'Escape') {
      store.selection.clearKeyframes();
    } else if ((e.ctrlKey || e.metaKey) && e.key.toLowerCase() === 'c') {
      const entries = selectedKeyframeEntries();
      if (entries.length) copyKeyframes(entries, store.playhead);
    } else if ((e.ctrlKey || e.metaKey) && e.key.toLowerCase() === 'v' && hasClipboard()) {
      e.preventDefault();
      const pasted = e.shiftKey ? pasteAtOriginalTimes() : pasteAtPlayhead(store.playhead);
      store.pasteKeyframes(pasted);
    }
  }
</script>

<div class="timelineToolbar">
  <label><span>Zoom</span><input type="range" min="10" max="400" bind:value={pxPerSec} /></label>
  <label class="magnet"><input type="checkbox" bind:checked={snapEnabled} /> Snap</label>
  <span class="hint">Click ruler to scrub · drag keyframes · double-click a lane to add one · Delete removes selection</span>
</div>
<div class="timelineWrap">
  <canvas
    bind:this={canvas}
    tabindex="0"
    onmousedown={onPointerDown}
    onmousemove={onPointerMove}
    onmouseup={onPointerUp}
    onmouseleave={onPointerUp}
    ondblclick={onDblClick}
    onkeydown={onKeyDown}
  ></canvas>
</div>

<style>
  .timelineToolbar {
    display: flex;
    align-items: center;
    gap: 12px;
    padding: 4px 8px;
    font: 11px var(--mono);
    color: var(--dim);
  }
  .timelineToolbar label {
    display: flex;
    align-items: center;
    gap: 4px;
  }
  .magnet {
    cursor: pointer;
  }
  .hint {
    margin-left: auto;
    opacity: 0.7;
  }
  .timelineWrap {
    overflow-x: auto;
    overflow-y: hidden;
    border-top: 1px solid var(--line);
  }
  canvas {
    display: block;
    cursor: pointer;
  }
  canvas:focus {
    outline: none;
  }
</style>
