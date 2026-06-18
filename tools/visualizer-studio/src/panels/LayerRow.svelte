<script lang="ts">
  import type { AnyLayer } from '../types/project';
  import type { ProjectStore } from '../state/project.svelte';
  import { confirmDialog } from '../state/confirmModal.svelte';
  import { openContextMenu } from '../timeline/contextMenu.svelte';

  let {
    store,
    layer,
    isFirst,
    isLast,
    onReorder,
    onMoveUp,
    onMoveDown,
  }: {
    store: ProjectStore;
    layer: AnyLayer;
    isFirst: boolean;
    isLast: boolean;
    onReorder: (draggedId: string, ontoId: string) => void;
    onMoveUp: () => void;
    onMoveDown: () => void;
  } = $props();

  let renaming = $state(false);
  let nameInput: HTMLInputElement | undefined = $state();

  function startRename() {
    renaming = true;
    queueMicrotask(() => nameInput?.focus());
  }

  function commitRename() {
    if (nameInput) store.renameLayer(layer.id, nameInput.value || layer.name);
    renaming = false;
  }

  async function onDelete() {
    const ok = await confirmDialog({
      title: 'Delete layer?',
      body: `Delete "${layer.name}"? You can undo this with Ctrl+Z.`,
      confirmLabel: 'Delete',
      danger: true,
    });
    if (ok) store.deleteLayer(layer.id);
  }

  function onDragStart(e: DragEvent) {
    e.dataTransfer?.setData('text/layer-id', layer.id);
  }
  function onDragOver(e: DragEvent) {
    e.preventDefault();
  }
  function onDrop(e: DragEvent) {
    e.preventDefault();
    const draggedId = e.dataTransfer?.getData('text/layer-id');
    if (draggedId && draggedId !== layer.id) onReorder(draggedId, layer.id);
  }

  function onContextMenu(e: MouseEvent) {
    e.preventDefault();
    openContextMenu(e.clientX, e.clientY, [
      { label: 'Rename', action: startRename },
      { label: 'Duplicate', action: () => store.duplicateLayer(layer.id) },
      { label: layer.locked ? 'Unlock' : 'Lock', action: () => store.toggleLayerLock(layer.id) },
      { label: store.soloedLayerIds.has(layer.id) ? 'Unsolo' : 'Solo', action: () => store.toggleLayerSolo(layer.id) },
      { label: 'Delete', action: onDelete, danger: true },
    ]);
  }
</script>

<div
  class="layerRow"
  class:selected={store.selection.layerId === layer.id}
  role="group"
  aria-label={layer.name}
  draggable="true"
  ondragstart={onDragStart}
  ondragover={onDragOver}
  ondrop={onDrop}
  oncontextmenu={onContextMenu}
>
  <button class="icon" title={layer.visible ? 'Hide' : 'Show'} onclick={() => store.toggleLayerVisibility(layer.id)}>
    {layer.visible ? '👁' : '—'}
  </button>
  <button class="icon" title={layer.locked ? 'Unlock' : 'Lock'} onclick={() => store.toggleLayerLock(layer.id)}>
    {layer.locked ? '🔒' : '🔓'}
  </button>
  {#if renaming}
    <input
      bind:this={nameInput}
      class="nameInput"
      value={layer.name}
      onblur={commitRename}
      onkeydown={(e) => {
        if (e.key === 'Enter') commitRename();
        if (e.key === 'Escape') renaming = false;
      }}
    />
  {:else}
    <button
      class="name"
      class:solo={store.soloedLayerIds.has(layer.id)}
      ondblclick={startRename}
      onclick={() => !layer.locked && store.selection.selectLayer(layer.id)}
    >
      {layer.name}
    </button>
  {/if}
  <button class="icon ghost" title="Move up" disabled={isFirst} onclick={onMoveUp}>↑</button>
  <button class="icon ghost" title="Move down" disabled={isLast} onclick={onMoveDown}>↓</button>
  <button class="icon ghost" title="Duplicate" onclick={() => store.duplicateLayer(layer.id)}>⧉</button>
  <button class="icon ghost danger" title="Delete" onclick={onDelete}>✕</button>
</div>

<style>
  .layerRow {
    display: flex;
    align-items: center;
    gap: 3px;
    padding: 3px 4px;
    border-radius: 3px;
  }
  .layerRow:hover {
    background: var(--bg2);
  }
  .layerRow.selected {
    box-shadow: inset 3px 0 0 var(--accent);
    background: var(--bg2);
  }
  .icon {
    width: 20px;
    height: 20px;
    padding: 0;
    background: none;
    border: none;
    color: var(--dim);
  }
  .icon.ghost {
    opacity: 0.6;
  }
  .icon.ghost:hover:not(:disabled) {
    opacity: 1;
  }
  .icon.danger:hover {
    color: var(--danger);
  }
  .name {
    flex: 1;
    text-align: left;
    background: none;
    border: none;
    color: var(--text);
    font: 11px var(--mono);
    overflow: hidden;
    text-overflow: ellipsis;
    white-space: nowrap;
  }
  .name.solo {
    color: var(--accent2);
  }
  .nameInput {
    flex: 1;
    font: 11px var(--mono);
  }
</style>
