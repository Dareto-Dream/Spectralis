<script lang="ts">
  import type { ProjectStore } from '../state/project.svelte';
  import { ANIM_KEYS } from '../types/project';
  import PropRow from './PropRow.svelte';
  import ParamFields from './ParamFields.svelte';
  import ShapeFields from './ShapeFields.svelte';
  import CurveEditor from '../timeline/CurveEditor.svelte';
  import type { Ease } from '../types/project';
  import { rasterizeLayer } from '../lib/rasterize';
  import { toast } from '../state/toast.svelte';
  import { toolState } from '../state/toolState.svelte';
  import Eye from '@lucide/svelte/icons/eye';
  import EyeOff from '@lucide/svelte/icons/eye-off';
  import Lock from '@lucide/svelte/icons/lock';
  import Unlock from '@lucide/svelte/icons/unlock';
  import Timer from '@lucide/svelte/icons/timer';
  import ImageDown from '@lucide/svelte/icons/image-down';

  let { store }: { store: ProjectStore } = $props();

  const singleSelectedKeyframeId = $derived(
    store.selection.keyframeIds.size === 1 ? [...store.selection.keyframeIds][0] : null
  );
  const singleSelectedEase = $derived.by((): Ease | null => {
    if (!singleSelectedKeyframeId) return null;
    const ref = store.selection.keyframeIndex.get(singleSelectedKeyframeId);
    return ref ? ref.layer.tracks[ref.trackKey][ref.index].ease : null;
  });

  const LABELS: Record<(typeof ANIM_KEYS)[number], string> = {
    x: 'X',
    y: 'Y',
    scale: 'Scale',
    rotation: 'Rotation',
    opacity: 'Opacity',
    hueA: 'Hue A',
    hueB: 'Hue B',
  };

  const layer = $derived(store.selection.selectedLayer);
  const selectedShape = $derived(
    layer && layer.type === 'vector' ? layer.params.shapes.find((s) => s.id === toolState.selectedShapeId) : undefined
  );

  function onDeleteShape() {
    if (!layer || layer.type !== 'vector' || !selectedShape) return;
    const id = selectedShape.id;
    layer.params.shapes = layer.params.shapes.filter((s) => s.id !== id);
    toolState.selectedShapeId = null;
    store.commit();
  }

  function onNameChange(e: Event) {
    if (!layer) return;
    store.renameLayer(layer.id, (e.target as HTMLInputElement).value);
  }

  function onRasterize() {
    if (!layer) return;
    rasterizeLayer(store, layer.id);
    toast.push('success', 'Rasterized to a paintable image layer');
  }
</script>

<div class="inspector">
  {#if !layer}
    <p class="hint">
      Select a layer to edit its properties. Click the stopwatch next to a property to enable keyframing — its lane
      then appears in the timeline below, where you can add and drag keyframes.
    </p>
  {:else}
    <div class="head">
      <input class="name" value={layer.name} onchange={onNameChange} />
      <button
        class="iconBtn"
        title={layer.visible ? 'Hide' : 'Show'}
        aria-label={layer.visible ? 'Hide layer' : 'Show layer'}
        onclick={() => store.toggleLayerVisibility(layer.id)}
      >
        {#if layer.visible}<Eye size={13} />{:else}<EyeOff size={13} />{/if}
      </button>
      <button
        class="iconBtn"
        title={layer.locked ? 'Unlock' : 'Lock (protect from canvas/timeline drag)'}
        aria-label={layer.locked ? 'Unlock layer' : 'Lock layer'}
        onclick={() => store.toggleLayerLock(layer.id)}
      >
        {#if layer.locked}<Lock size={13} />{:else}<Unlock size={13} />{/if}
      </button>
      <button
        class="iconBtn"
        class:on={store.soloedLayerIds.has(layer.id)}
        title="Solo (isolate this layer in the preview)"
        aria-label={store.soloedLayerIds.has(layer.id) ? 'Unsolo layer' : 'Solo layer'}
        onclick={() => store.toggleLayerSolo(layer.id)}
      >
        S
      </button>
      {#if layer.type === 'vector'}
        <button class="iconBtn" title="Rasterize — bake to a paintable image, replaces this layer" aria-label="Rasterize layer" onclick={onRasterize}>
          <ImageDown size={13} />
        </button>
      {/if}
    </div>

    <div class="visRow">
      <button
        class="kbtn"
        class:on={store.isVisibilityKeyframed(layer.id)}
        title={store.isVisibilityKeyframed(layer.id) ? 'Visibility is keyframed — click to disable' : 'Enable show/hide keyframing'}
        aria-label={store.isVisibilityKeyframed(layer.id) ? 'Visibility keyframed, click to disable' : 'Enable show/hide keyframing'}
        onclick={() => store.toggleVisibilityKeyframing(layer.id)}
      >
        <Timer size={12} />
      </button>
      <span class="visLabel">Visibility</span>
      {#if store.isVisibilityKeyframed(layer.id)}
        <span class="kfCount">{layer.visibleTrack?.length ?? 0} keys</span>
        <button class="small" title="Add a show/hide toggle at playhead" onclick={() => store.addVisibilityToggleAtPlayhead(layer.id)}>+Key</button>
      {/if}
    </div>

    <div class="props">
      {#each ANIM_KEYS as key (key)}
        <PropRow {store} {layer} propKey={key} label={LABELS[key]} />
      {/each}
    </div>

    <ParamFields {layer} onChange={() => store.commit()} />

    {#if layer.type === 'vector' && selectedShape}
      <div class="shapeSection">
        <p class="sectionLabel">Selected Shape</p>
        <ShapeFields {store} {layer} shape={selectedShape} onChange={() => store.commit()} onDelete={onDeleteShape} />
      </div>
    {/if}

    {#if singleSelectedKeyframeId && singleSelectedEase}
      <div class="curveSection">
        <p class="sectionLabel">Ease</p>
        <CurveEditor value={singleSelectedEase} onSelect={(ease) => store.setKeyframeEase(singleSelectedKeyframeId, ease)} />
      </div>
    {/if}
  {/if}
</div>

<style>
  .inspector {
    padding: 8px;
    display: flex;
    flex-direction: column;
    gap: 10px;
  }
  .hint {
    font: 11px var(--mono);
    color: var(--dim);
    line-height: 1.5;
  }
  .head {
    display: flex;
    align-items: center;
    gap: 4px;
  }
  .name {
    flex: 1;
    font: 12px var(--mono);
  }
  .iconBtn {
    width: 24px;
    height: 24px;
    padding: 0;
    justify-content: center;
    background: var(--bg3);
    border: 1px solid var(--line);
    border-radius: 3px;
  }
  .iconBtn.on {
    background: var(--accent2);
    color: #1a1400;
  }
  .visRow {
    display: flex;
    align-items: center;
    gap: 6px;
    padding: 2px 4px;
  }
  .visLabel {
    flex: 1;
    font: 11px var(--mono);
    color: var(--text);
  }
  .kbtn {
    background: none;
    border: 1px solid var(--line);
    border-radius: 3px;
    color: var(--dim);
    width: 20px;
    height: 20px;
    padding: 0;
    justify-content: center;
  }
  .kbtn.on {
    background: var(--accent2);
    color: #1a1400;
    border-color: var(--accent2);
  }
  .kfCount {
    font: 10px var(--mono);
    color: var(--accent);
  }
  .props {
    display: flex;
    flex-direction: column;
    gap: 2px;
    border-top: 1px solid var(--line);
    border-bottom: 1px solid var(--line);
    padding: 4px 0;
  }
  .shapeSection,
  .curveSection {
    border-top: 1px solid var(--line);
    padding-top: 6px;
  }
  .sectionLabel {
    margin: 0 0 2px;
    font: 10px var(--mono);
    color: var(--dim2);
    text-transform: uppercase;
    letter-spacing: 0.05em;
  }
</style>
