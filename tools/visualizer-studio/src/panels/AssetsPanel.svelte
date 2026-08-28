<script lang="ts">
  import { assetLibrary } from '../state/assetLibrary.svelte';
  import { dockManager } from './dockManager.svelte';
  import { dropZone } from '../lib/dropZone';
  import { startAssetDrag } from '../lib/dragAsset';
  import { toast } from '../state/toast.svelte';
  import type { AssetKind } from '../types/asset';
  import Upload from '@lucide/svelte/icons/upload';
  import FileCode from '@lucide/svelte/icons/file-code';
  import Search from '@lucide/svelte/icons/search';
  import Image from '@lucide/svelte/icons/image';
  import Music from '@lucide/svelte/icons/music';
  import X from '@lucide/svelte/icons/x';

  let fileInput: HTMLInputElement | undefined = $state();
  let query = $state('');
  let kindFilter: AssetKind | 'all' = $state('all');
  let renamingId: string | null = $state(null);
  let renameValue = $state('');

  const filtered = $derived(
    assetLibrary.assets.filter(
      (a) => (kindFilter === 'all' || a.kind === kindFilter) && a.name.toLowerCase().includes(query.toLowerCase())
    )
  );

  async function addFiles(files: FileList | File[]) {
    for (const f of Array.from(files)) await assetLibrary.addFile(f);
    toast.push('success', `Added ${files.length} asset${files.length === 1 ? '' : 's'}`);
  }

  function onPick(e: Event) {
    const files = (e.target as HTMLInputElement).files;
    if (files?.length) addFiles(files);
    (e.target as HTMLInputElement).value = '';
  }

  function startRename(id: string, current: string) {
    renamingId = id;
    renameValue = current;
  }
  function commitRename() {
    if (renamingId) assetLibrary.rename(renamingId, renameValue);
    renamingId = null;
  }

  function fmtSize(bytes: number): string {
    if (bytes < 1024) return `${bytes} B`;
    if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`;
    return `${(bytes / 1024 / 1024).toFixed(1)} MB`;
  }
</script>

<div
  class="assetsPanel"
  use:dropZone={{
    accept: () => true,
    onDrop: (f) => addFiles([f]),
    onReject: () => toast.push('error', "Couldn't read that file"),
  }}
>
  <div class="toolbar">
    <button class="small ghost" onclick={() => fileInput?.click()}><Upload size={12} /> Add Asset</button>
    <input bind:this={fileInput} type="file" multiple hidden onchange={onPick} />
    <button class="small ghost" onclick={() => dockManager.focusOrOpen('svgmaker')}><FileCode size={12} /> New SVG</button>
    <span class="sep"></span>
    <div class="kindFilter">
      {#each [['all', 'All'], ['image', 'Image'], ['audio', 'Audio'], ['svg', 'SVG']] as [id, label] (id)}
        <button class="small ghost" class:active={kindFilter === id} onclick={() => (kindFilter = id as AssetKind | 'all')}>{label}</button>
      {/each}
    </div>
    <span class="sep"></span>
    <span class="spacer"></span>
    <div class="search">
      <Search size={12} />
      <input type="text" placeholder="filter…" bind:value={query} />
    </div>
  </div>
  <div class="grid">
    {#if !assetLibrary.assets.length}
      <p class="empty">No assets yet. Drop files here, use "Add Asset", or save something from the SVG Maker.</p>
    {:else if !filtered.length}
      <p class="empty">Nothing matches.</p>
    {:else}
      {#each filtered as a (a.id)}
        <!-- svelte-ignore a11y_no_static_element_interactions -->
        <div class="card" draggable="true" ondragstart={(e) => startAssetDrag(e, a)} title="Drag onto a cover slot, track field, etc.">
          <div class="thumb">
            {#if a.kind === 'audio'}
              <Music size={22} />
            {:else}
              <img src={a.dataUrl} alt={a.name} />
            {/if}
          </div>
          {#if renamingId === a.id}
            <input
              class="nameInput"
              value={renameValue}
              oninput={(e) => (renameValue = (e.target as HTMLInputElement).value)}
              onblur={commitRename}
              onkeydown={(e) => {
                if (e.key === 'Enter') commitRename();
                if (e.key === 'Escape') renamingId = null;
              }}
            />
          {:else}
            <button class="name" ondblclick={() => startRename(a.id, a.name)} title="Double-click to rename">{a.name}</button>
          {/if}
          <span class="size">{fmtSize(a.size)}</span>
          <button class="icon ghost danger remove" title="Remove" aria-label={`Remove ${a.name}`} onclick={() => assetLibrary.remove(a.id)}>
            <X size={11} />
          </button>
        </div>
      {/each}
    {/if}
  </div>
</div>

<style>
  .assetsPanel {
    display: flex;
    flex-direction: column;
    height: 100%;
  }
  .toolbar {
    display: flex;
    align-items: center;
    gap: 6px;
    padding: 6px 8px;
    border-bottom: 1px solid var(--line);
    flex-shrink: 0;
  }
  .sep {
    width: 1px;
    align-self: stretch;
    background: var(--line);
    margin: 0 2px;
  }
  .kindFilter {
    display: flex;
    gap: 2px;
  }
  .kindFilter button.active {
    background: var(--bg3);
    color: var(--accent);
  }
  .spacer {
    flex: 1;
  }
  .search {
    display: flex;
    align-items: center;
    gap: 4px;
    color: var(--dim2);
  }
  .search input {
    width: 120px;
  }
  .grid {
    flex: 1;
    overflow-y: auto;
    display: flex;
    flex-wrap: wrap;
    align-content: flex-start;
    gap: 8px;
    padding: 8px;
  }
  .empty {
    width: 100%;
    padding: 20px;
    text-align: center;
    font: 11px var(--mono);
    color: var(--dim2);
  }
  .card {
    position: relative;
    width: 84px;
    display: flex;
    flex-direction: column;
    align-items: center;
    gap: 2px;
    padding: 6px;
    border-radius: 4px;
    border: 1px solid transparent;
    cursor: grab;
  }
  .card:hover {
    background: var(--bg2);
    border-color: var(--line);
  }
  .card:hover .remove {
    opacity: 1;
  }
  .thumb {
    width: 56px;
    height: 56px;
    display: flex;
    align-items: center;
    justify-content: center;
    background: var(--bg3);
    border: 1px solid var(--line);
    border-radius: 3px;
    color: var(--dim);
    overflow: hidden;
  }
  .thumb img {
    max-width: 100%;
    max-height: 100%;
    object-fit: contain;
  }
  .name {
    width: 100%;
    background: none;
    border: none;
    color: var(--text);
    font: 10px var(--mono);
    text-align: center;
    overflow: hidden;
    text-overflow: ellipsis;
    white-space: nowrap;
  }
  .nameInput {
    width: 100%;
    font: 10px var(--mono);
    text-align: center;
  }
  .size {
    font: 9px var(--mono);
    color: var(--dim2);
  }
  .remove {
    position: absolute;
    top: 2px;
    right: 2px;
    width: 16px;
    height: 16px;
    opacity: 0;
    background: var(--bg1);
  }
</style>
