<script lang="ts">
  import type { AnimKey, AnyLayer } from '../types/project';
  import type { ProjectStore } from '../state/project.svelte';
  import { scrubbable } from '../lib/scrubbableNumber';

  let { store, layer, propKey, label }: { store: ProjectStore; layer: AnyLayer; propKey: AnimKey; label: string } = $props();

  const keyframed = $derived(layer.tracks[propKey].length > 0);

  function onStaticChange(e: Event) {
    const v = parseFloat((e.target as HTMLInputElement).value);
    if (isNaN(v)) return;
    layer.statics[propKey] = v;
    store.commit();
  }

  function selectTrack() {
    store.selection.selectLayer(layer.id);
    store.selection.selectTrack(propKey);
  }
</script>

<div class="propRow" class:selected={store.selection.trackKey === propKey && store.selection.layerId === layer.id}>
  <button
    class="kbtn"
    class:on={keyframed}
    title={keyframed ? 'Keyframed — click to disable' : 'Enable keyframing'}
    aria-label={keyframed ? `${label}: keyframed, click to disable` : `Enable keyframing for ${label}`}
    onclick={() => store.toggleKeyframing(layer.id, propKey)}
  >
    ◎
  </button>
  {#if keyframed}
    <button class="label" onclick={selectTrack}>{label}</button>
  {:else}
    <button
      class="label scrub"
      title="Click to select track · drag to scrub value"
      use:scrubbable={{
        value: layer.statics[propKey],
        step: 0.5,
        onChange: (v: number) => (layer.statics[propKey] = v),
        onCommit: () => store.commit(),
        onClick: selectTrack,
      }}
    >
      {label}
    </button>
  {/if}
  {#if keyframed}
    <button class="kfCount" onclick={selectTrack}>{layer.tracks[propKey].length} keys</button>
    <button class="small" title="Add key at playhead" onclick={() => store.addKeyframeAtPlayhead(layer.id, propKey)}>+Key</button>
  {:else}
    <input type="number" step="0.5" value={layer.statics[propKey]} onchange={onStaticChange} />
  {/if}
</div>

<style>
  .propRow {
    display: flex;
    align-items: center;
    gap: 6px;
    padding: 2px 4px;
    border-radius: 3px;
  }
  .propRow.selected {
    background: var(--bg3);
  }
  .kbtn {
    background: none;
    border: 1px solid var(--line);
    border-radius: 3px;
    color: var(--dim);
    width: 20px;
    height: 20px;
    padding: 0;
  }
  .kbtn.on {
    background: var(--accent2);
    color: #1a1400;
    border-color: var(--accent2);
  }
  .label {
    flex: 1;
    text-align: left;
    background: none;
    border: none;
    color: var(--text);
    font: 11px var(--mono);
  }
  .kfCount {
    background: none;
    border: none;
    color: var(--accent);
    font: 10px var(--mono);
  }
  .label.scrub {
    cursor: ew-resize;
  }
  .label.scrub.scrubbing,
  .label.scrub:hover {
    color: var(--accent);
  }
  input[type='number'] {
    width: 70px;
  }
</style>
