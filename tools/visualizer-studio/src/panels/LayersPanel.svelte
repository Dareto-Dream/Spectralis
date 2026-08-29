<script lang="ts">
  import type { ProjectStore } from '../state/project.svelte';
  import { LAYER_TYPE_DEFS, LAYER_TYPES } from '../types/layerDefs';
  import type { LayerType } from '../types/project';
  import LayerRow from './LayerRow.svelte';

  let { store }: { store: ProjectStore } = $props();

  let newLayerType: LayerType = $state('vector');

  // Old tool painted the list topmost-layer-first (matching on-canvas stacking:
  // top of list = top of stack), which is the REVERSE of the underlying array
  // (last element draws on top). "Move up" in the displayed list therefore means
  // "move later in the real array" — moveLayer's sign is flipped accordingly.
  const displayLayers = $derived([...store.project.layers].reverse());
</script>

<div class="layersPanel">
  <div class="addRow">
    <select bind:value={newLayerType}>
      {#each LAYER_TYPES as t (t)}
        <option value={t}>{LAYER_TYPE_DEFS[t].label}</option>
      {/each}
    </select>
    <button onclick={() => store.addLayer(newLayerType)}>+ Add</button>
  </div>

  <div class="list">
    {#if displayLayers.length === 0}
      <p class="empty">No layers yet. Use the + menu above to add one.</p>
    {:else}
      {#each displayLayers as layer, i (layer.id)}
        <LayerRow
          {store}
          {layer}
          isFirst={i === 0}
          isLast={i === displayLayers.length - 1}
          onReorder={(draggedId, ontoId) => store.reorderLayer(draggedId, ontoId)}
          onMoveUp={() => store.moveLayer(layer.id, 1)}
          onMoveDown={() => store.moveLayer(layer.id, -1)}
        />
      {/each}
    {/if}
  </div>
</div>

<style>
  .layersPanel {
    display: flex;
    flex-direction: column;
    height: 100%;
  }
  .addRow {
    display: flex;
    gap: 6px;
    padding: 6px;
    border-bottom: 1px solid var(--line);
  }
  .addRow select {
    flex: 1;
  }
  .list {
    overflow-y: auto;
    padding: 4px;
  }
  .empty {
    padding: 12px 6px;
    text-align: center;
    font: 11px var(--mono);
    color: var(--dim2);
  }
</style>
