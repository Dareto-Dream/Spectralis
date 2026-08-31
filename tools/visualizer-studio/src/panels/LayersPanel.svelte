<script lang="ts">
  import type { ProjectStore } from '../state/project.svelte';
  import { LAYER_TYPE_DEFS, LAYER_TYPES } from '../types/layerDefs';
  import type { AnyLayer, LayerGroup, LayerType } from '../types/project';
  import LayerRow from './LayerRow.svelte';
  import ChevronRight from '@lucide/svelte/icons/chevron-right';
  import ChevronDown from '@lucide/svelte/icons/chevron-down';

  let { store }: { store: ProjectStore } = $props();

  let newLayerType: LayerType = $state('vector');

  // Old tool painted the list topmost-layer-first (matching on-canvas stacking:
  // top of list = top of stack), which is the REVERSE of the underlying array
  // (last element draws on top). "Move up" in the displayed list therefore means
  // "move later in the real array" — moveLayer's sign is flipped accordingly.
  const displayLayers = $derived([...store.project.layers].reverse());

  // Clusters CONSECUTIVE same-groupId rows (in display order) under one
  // collapsible header — a group only ever gets rendered as a group while
  // its members are still adjacent, which is how the Lyrics Importer and
  // Layer Templates always insert them (state/project.svelte.ts's
  // addLayers); reordering an individual member away from its siblings
  // just quietly demotes it back to a plain standalone row, matching the
  // "flat array with an optional groupId, no recursive tree" design.
  type DisplayRow =
    | { kind: 'group'; group: LayerGroup; members: { layer: AnyLayer; index: number }[] }
    | { kind: 'layer'; layer: AnyLayer; index: number };

  const displayRows = $derived.by((): DisplayRow[] => {
    const groupById = new Map((store.project.layerGroups ?? []).map((g) => [g.id, g]));
    const rows: DisplayRow[] = [];
    let i = 0;
    while (i < displayLayers.length) {
      const gid = displayLayers[i].groupId;
      const group = gid ? groupById.get(gid) : undefined;
      if (group) {
        const members: { layer: AnyLayer; index: number }[] = [];
        while (i < displayLayers.length && displayLayers[i].groupId === gid) {
          members.push({ layer: displayLayers[i], index: i });
          i++;
        }
        rows.push({ kind: 'group', group, members });
      } else {
        rows.push({ kind: 'layer', layer: displayLayers[i], index: i });
        i++;
      }
    }
    return rows;
  });
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
      {#each displayRows as row (row.kind === 'group' ? row.group.id : row.layer.id)}
        {#if row.kind === 'group'}
          <button
            class="groupHeader"
            onclick={() => store.toggleLayerGroupCollapsed(row.group.id)}
            aria-label={row.group.collapsed ? `Expand ${row.group.name}` : `Collapse ${row.group.name}`}
          >
            {#if row.group.collapsed}<ChevronRight size={13} />{:else}<ChevronDown size={13} />{/if}
            <span class="groupName">{row.group.name}</span>
            <span class="groupCount">{row.members.length}</span>
          </button>
          {#if !row.group.collapsed}
            {#each row.members as m (m.layer.id)}
              <div class="groupMember">
                <LayerRow
                  {store}
                  layer={m.layer}
                  isFirst={m.index === 0}
                  isLast={m.index === displayLayers.length - 1}
                  onReorder={(draggedId, ontoId) => store.reorderLayer(draggedId, ontoId)}
                  onMoveUp={() => store.moveLayer(m.layer.id, 1)}
                  onMoveDown={() => store.moveLayer(m.layer.id, -1)}
                />
              </div>
            {/each}
          {/if}
        {:else}
          <LayerRow
            {store}
            layer={row.layer}
            isFirst={row.index === 0}
            isLast={row.index === displayLayers.length - 1}
            onReorder={(draggedId, ontoId) => store.reorderLayer(draggedId, ontoId)}
            onMoveUp={() => store.moveLayer(row.layer.id, 1)}
            onMoveDown={() => store.moveLayer(row.layer.id, -1)}
          />
        {/if}
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
  .groupHeader {
    display: flex;
    align-items: center;
    gap: 4px;
    width: 100%;
    padding: 4px 6px;
    margin-top: 2px;
    background: var(--bg2);
    border: 1px solid var(--line);
    border-radius: 4px;
    color: var(--dim);
    font: 11px var(--mono);
    text-align: left;
  }
  .groupHeader:hover {
    color: var(--text);
    border-color: var(--line2);
  }
  .groupName {
    flex: 1;
    overflow: hidden;
    text-overflow: ellipsis;
    white-space: nowrap;
  }
  .groupCount {
    color: var(--dim2);
    font-size: 10px;
  }
  .groupMember {
    padding-left: 12px;
    border-left: 1px solid var(--line);
    margin-left: 6px;
  }
</style>
