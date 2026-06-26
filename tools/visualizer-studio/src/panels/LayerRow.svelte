<script lang="ts">
  import type { AnyLayer } from '../types/project';
  import type { ProjectStore } from '../state/project.svelte';
  import { confirmDialog } from '../state/confirmModal.svelte';
  import { openContextMenu } from '../timeline/contextMenu.svelte';
  import Eye from '@lucide/svelte/icons/eye';
  import EyeOff from '@lucide/svelte/icons/eye-off';
  import Lock from '@lucide/svelte/icons/lock';
  import Unlock from '@lucide/svelte/icons/unlock';
  import ChevronUp from '@lucide/svelte/icons/chevron-up';
  import ChevronDown from '@lucide/svelte/icons/chevron-down';
  import Copy from '@lucide/svelte/icons/copy';
  import X from '@lucide/svelte/icons/x';

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

  // F2 (lib/keymap.ts) bumps store.selection.renameRequestId — react only
  // when this row's layer is the one currently selected.
  $effect(() => {
    void store.selection.renameRequestId;
    if (store.selection.renameRequestId > 0 && store.selection.layerId === layer.id) startRename();
  });

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
  <button
    class="icon"
    title={layer.visible ? 'Hide' : 'Show'}
    aria-label={layer.visible ? `Hide ${layer.name}` : `Show ${layer.name}`}
    onclick={() => store.toggleLayerVisibility(layer.id)}
  >
    {#if layer.visible}<Eye size={13} />{:else}<EyeOff size={13} />{/if}
  </button>
  <button
    class="icon"
    title={layer.locked ? 'Unlock' : 'Lock'}
    aria-label={layer.locked ? `Unlock ${layer.name}` : `Lock ${layer.name}`}
    onclick={() => store.toggleLayerLock(layer.id)}
  >
    {#if layer.locked}<Lock size={13} />{:else}<Unlock size={13} />{/if}
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
  <button class="icon ghost" title="Move up" aria-label={`Move ${layer.name} up`} disabled={isFirst} onclick={onMoveUp}><ChevronUp size={13} /></button>
  <button class="icon ghost" title="Move down" aria-label={`Move ${layer.name} down`} disabled={isLast} onclick={onMoveDown}><ChevronDown size={13} /></button>
  <button class="icon ghost" title="Duplicate" aria-label={`Duplicate ${layer.name}`} onclick={() => store.duplicateLayer(layer.id)}><Copy size={13} /></button>
  <button class="icon ghost danger" title="Delete" aria-label={`Delete ${layer.name}`} onclick={onDelete}><X size={13} /></button>
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
