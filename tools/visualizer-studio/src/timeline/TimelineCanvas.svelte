<script lang="ts">
  import type { ProjectStore } from '../state/project.svelte';
  import type { AudioState } from '../state/audio.svelte';
  import { computeLayout, bandAtY } from './layout';
  import { drawTimeline } from './draw';
  import {
    hitTestActiveLaneKeyframe,
    hitTestOverviewRow,
    hitTestSection,
    hitTestSectionEdge,
    hitTestScrub,
    keyframesInMarquee,
    cursorAt,
    type MarqueeRect,
    type SectionEdge,
  } from './interactions';
  import { snap, snapTargets } from './snapping';
  import { copyKeyframes, pasteAtPlayhead, pasteAtOriginalTimes, hasClipboard } from './clipboard';
  import { openContextMenu, type ContextMenuItem } from './contextMenu.svelte';
  import { EASE_NAMES } from './curveEditor';
  import { confirmDialog } from '../state/confirmModal.svelte';
  import { toast } from '../state/toast.svelte';
  import { dropZone } from '../lib/dropZone';
  import { fmtTime } from '../lib/fmtTime';
  import { onMount } from 'svelte';

  let { store, audio }: { store: ProjectStore; audio: AudioState } = $props();

  let canvas: HTMLCanvasElement;
  let wrapper: HTMLDivElement;
  let ctx: CanvasRenderingContext2D;
  let ready = $state(false);

  onMount(() => {
    ctx = canvas.getContext('2d')!;
    ready = true;
  });
  // Lifted onto the store (not local state) so the global `+`/`-` shortcuts in
  // lib/keymap.ts can reach the current zoom without a component reference.
  const pxPerSec = $derived(store.timelinePxPerSec);
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

    // Playback QoL §J: keep the playhead in view once zoom overflows the
    // panel width, instead of letting it silently scroll off-screen.
    if (store.playing && wrapper) {
      const playheadX = store.playhead * pxPerSec;
      const margin = 40;
      if (playheadX < wrapper.scrollLeft + margin || playheadX > wrapper.scrollLeft + wrapper.clientWidth - margin) {
        wrapper.scrollLeft = Math.max(0, playheadX - wrapper.clientWidth / 2);
      }
    }
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

    const activeLayer = store.project.layers.find((l) => l.id === store.selection.layerId);
    const kfHit = activeLayer?.locked
      ? null
      : hitTestActiveLaneKeyframe(layout, store.project, store.selection.layerId, store.selection.trackKey, pxPerSec, x, y);
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
    if (rowHit && !rowHit.locked) {
      // Overview/minimap rows are click-to-seek-and-select-lane, not just
      // visual (plan QoL §H).
      store.selection.selectLayer(rowHit.id);
      store.seekTo(t);
      return;
    }

    if (store.selection.layerId && store.selection.trackKey) {
      drag = { kind: 'marquee', rect: { x0: x, y0: y, x1: x, y1: y } };
      store.selection.clearKeyframes();
    }
  }

  function onPointerMove(e: MouseEvent) {
    const { x, y } = localPoint(e);

    // Region-aware cursor feedback (plan QoL §H) — updates on every move,
    // dragging or not, so hovering alone previews what a click/drag would do.
    canvas.style.cursor = cursorAt(
      currentLayout(),
      store.project,
      store.selection.layerId,
      store.selection.trackKey,
      pxPerSec,
      x,
      y,
      drag?.kind ?? null
    );

    if (!drag) return;
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

  // Scroll-wheel zoom (plan QoL §H), anchored at the cursor like AE/most DAWs
  // — supplements the slider rather than replacing it. Deferred to the next
  // frame so the canvas has already resized for the new pxPerSec before we
  // set scrollLeft (setting it against the old, narrower width would clamp).
  function onWheel(e: WheelEvent) {
    e.preventDefault();
    const wrapperRect = wrapper.getBoundingClientRect();
    const localXInWrapper = e.clientX - wrapperRect.left;
    const t = (wrapper.scrollLeft + localXInWrapper) / pxPerSec;
    const factor = e.deltaY < 0 ? 1.15 : 1 / 1.15;
    const next = Math.max(10, Math.min(400, pxPerSec * factor));
    store.timelinePxPerSec = next;
    requestAnimationFrame(() => {
      wrapper.scrollLeft = Math.max(0, t * next - localXInWrapper);
    });
  }

  // Right-click context menus (plan QoL §H) — keyframe (Delete / Copy / Set
  // Ease), section (Edit / Loop / Delete), or empty lane space with a
  // non-empty clipboard (Paste). Whichever the cursor is over wins, checked
  // in the same priority order onPointerDown already uses.
  function onContextMenu(e: MouseEvent) {
    const { x, y } = localPoint(e);
    const layout = currentLayout();

    const kfHit = hitTestActiveLaneKeyframe(layout, store.project, store.selection.layerId, store.selection.trackKey, pxPerSec, x, y);
    if (kfHit) {
      e.preventDefault();
      if (!store.selection.keyframeIds.has(kfHit.keyframeId)) {
        store.selection.setKeyframeSelection([kfHit.keyframeId]);
      }
      const items: ContextMenuItem[] = [
        {
          label: 'Copy',
          action: () => {
            const entries = [...store.selection.keyframeIds]
              .map((id) => store.selection.keyframeIndex.get(id))
              .filter((ref): ref is NonNullable<typeof ref> => !!ref)
              .map((ref) => ({ layerId: ref.layer.id, trackKey: ref.trackKey, ...ref.layer.tracks[ref.trackKey][ref.index] }));
            if (entries.length) copyKeyframes(entries, store.playhead);
          },
        },
        {
          label: 'Delete',
          danger: true,
          action: () => {
            for (const id of [...store.selection.keyframeIds]) store.deleteKeyframe(id);
          },
        },
        ...EASE_NAMES.map((name) => ({ label: `Ease: ${name}`, action: () => store.setKeyframeEase(kfHit.keyframeId, name) })),
      ];
      openContextMenu(e.clientX, e.clientY, items);
      return;
    }

    const secHit = hitTestSection(layout, store.project, pxPerSec, x, y);
    if (secHit) {
      e.preventDefault();
      const sec = store.project.sections.find((s) => s.id === secHit);
      openContextMenu(e.clientX, e.clientY, [
        { label: 'Edit', action: () => store.selection.selectSection(secHit) },
        {
          label: 'Loop this section',
          action: () => {
            store.selection.selectSection(secHit);
            store.setLoopRegionToSelectedSection();
          },
        },
        {
          label: 'Delete',
          danger: true,
          disabled: store.project.sections.length <= 1,
          action: async () => {
            const ok = await confirmDialog({
              title: 'Delete section?',
              body: `Delete "${sec?.label}"? You can undo this with Ctrl+Z.`,
              confirmLabel: 'Delete',
              danger: true,
            });
            if (ok) {
              store.deleteSection(secHit);
              store.selection.selectSection(null);
            }
          },
        },
      ]);
      return;
    }

    if (bandAtY(layout, y) === 'lane' && hasClipboard() && store.selection.layerId && store.selection.trackKey) {
      e.preventDefault();
      const t = Math.max(0, x / pxPerSec);
      openContextMenu(e.clientX, e.clientY, [
        { label: 'Paste at cursor', action: () => store.pasteKeyframes(pasteAtPlayhead(t)) },
        { label: 'Paste at original time', action: () => store.pasteKeyframes(pasteAtOriginalTimes()) },
      ]);
    }
  }

  // Delete/Escape/Copy/Paste are handled by the app-wide keymap (lib/keymap.ts)
  // now — a local handler here would double-fire (double-paste is a real bug,
  // not just a harmless redundant delete) whenever the canvas itself has focus.
</script>

<div class="timelineToolbar">
  <button class="small" onclick={() => store.togglePlay()} title="Space" aria-label={store.playing ? 'Pause' : 'Play'}>{store.playing ? '⏸ Pause' : '▶ Play'}</button>
  <button class="small" onclick={() => store.stop()} aria-label="Stop">■ Stop</button>
  <button
    class="small"
    class:on={store.loopEnabled}
    onclick={() => store.toggleLoop()}
    title={store.loopRegion ? `Loop ${fmtTime(store.loopRegion.start)}–${fmtTime(store.loopRegion.end)} (select a section + right-click → "Loop this section" to change)` : 'Loop the current section'}
    aria-label={store.loopEnabled ? 'Disable loop' : 'Enable loop'}
  >
    🔁 Loop
  </button>
  <span class="readout">{fmtTime(store.playhead)}</span>
  <label>
    <span>Zoom</span>
    <input
      type="range"
      min="10"
      max="400"
      value={pxPerSec}
      oninput={(e) => (store.timelinePxPerSec = +(e.target as HTMLInputElement).value)}
      aria-label="Timeline zoom"
    />
  </label>
  <label class="magnet"><input type="checkbox" bind:checked={snapEnabled} /> Snap</label>
  <span class="hint">Click ruler to scrub · drag keyframes · double-click a lane to add one · right-click for more · Delete removes selection</span>
</div>
<div
  class="timelineWrap"
  bind:this={wrapper}
  title="Drop an audio file anywhere here to load it"
  use:dropZone={{
    accept: (f) => f.type.startsWith('audio/') || /\.(mp3|wav|ogg|m4a|flac|aac)$/i.test(f.name),
    onDrop: (f) => audio.loadFile(f),
    onReject: () => toast.push('error', "That doesn't look like an audio file"),
  }}
>
  <canvas
    bind:this={canvas}
    tabindex="0"
    onmousedown={onPointerDown}
    onmousemove={onPointerMove}
    onmouseup={onPointerUp}
    onmouseleave={onPointerUp}
    ondblclick={onDblClick}
    onwheel={onWheel}
    oncontextmenu={onContextMenu}
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
  .timelineToolbar .small.on {
    background: var(--accent2);
    color: #1a1400;
    border-color: var(--accent2);
  }
  .readout {
    font-variant-numeric: tabular-nums;
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
    cursor: default; /* live cursor feedback set in JS via cursorAt() (plan QoL §H) */
  }
  canvas:focus:not(:focus-visible) {
    outline: none;
  }
</style>
