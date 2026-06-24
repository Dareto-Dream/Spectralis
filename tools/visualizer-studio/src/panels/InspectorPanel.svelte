<script lang="ts">
  import type { ProjectStore } from '../state/project.svelte';
  import { ANIM_KEYS } from '../types/project';
  import PropRow from './PropRow.svelte';
  import ParamFields from './ParamFields.svelte';
  import CurveEditor from '../timeline/CurveEditor.svelte';
  import type { Ease } from '../types/project';
  import { importLrcFile } from '../lib/lrcImport';

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

  function onNameChange(e: Event) {
    if (!layer) return;
    store.renameLayer(layer.id, (e.target as HTMLInputElement).value);
  }
</script>

<div class="inspector">
  {#if !layer}
    <p class="hint">
      Select a layer to edit its properties. Click the ◎ next to a property to enable keyframing — its lane then
      appears in the timeline below, where you can add and drag keyframes.
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
        {layer.visible ? '👁' : '—'}
      </button>
      <button
        class="iconBtn"
        title={layer.locked ? 'Unlock' : 'Lock (protect from canvas/timeline drag)'}
        aria-label={layer.locked ? 'Unlock layer' : 'Lock layer'}
        onclick={() => store.toggleLayerLock(layer.id)}
      >
        {layer.locked ? '🔒' : '🔓'}
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
    </div>

    <div class="props">
      {#each ANIM_KEYS as key (key)}
        <PropRow {store} {layer} propKey={key} label={LABELS[key]} />
      {/each}
    </div>

    <ParamFields {layer} onChange={() => store.commit()} onDropLrc={(f) => importLrcFile(store, f)} />

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
    background: var(--bg3);
    border: 1px solid var(--line);
    border-radius: 3px;
  }
  .iconBtn.on {
    background: var(--accent2);
    color: #1a1400;
  }
  .props {
    display: flex;
    flex-direction: column;
    gap: 2px;
    border-top: 1px solid var(--line);
    border-bottom: 1px solid var(--line);
    padding: 4px 0;
  }
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
