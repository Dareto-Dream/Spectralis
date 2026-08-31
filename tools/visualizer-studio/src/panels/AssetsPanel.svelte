<script lang="ts">
  import type { ProjectStore } from '../state/project.svelte';
  import { assetLibrary } from '../state/assetLibrary.svelte';
  import { dockManager } from './dockManager.svelte';
  import { dropZone } from '../lib/dropZone';
  import { startAssetDrag } from '../lib/dragAsset';
  import { toast } from '../state/toast.svelte';
  import { TEMPLATES, LAYER_TEMPLATES } from '../lib/templates';
  import { loadTemplateIntoStore } from '../lib/projectImport';
  import { uiState } from '../state/uiState.svelte';
  import type { AssetKind } from '../types/asset';
  import Upload from '@lucide/svelte/icons/upload';
  import FileCode from '@lucide/svelte/icons/file-code';
  import Search from '@lucide/svelte/icons/search';
  import Image from '@lucide/svelte/icons/image';
  import Music from '@lucide/svelte/icons/music';
  import LayoutTemplate from '@lucide/svelte/icons/layout-template';
  import ScrollText from '@lucide/svelte/icons/scroll-text';
  import X from '@lucide/svelte/icons/x';

  // `store` is only needed for the Templates category (loading a full starter
  // project replaces store.project) — every other asset kind here is
  // independent of any one project.
  let { store }: { store?: ProjectStore } = $props();

  let fileInput: HTMLInputElement | undefined = $state();
  let query = $state('');
  // 'template' is not an AssetKind — full-project starters (lib/templates.ts)
  // aren't assets, but this is where anything preset-shaped belongs instead
  // of being silently applied on load (see App.svelte's blank-boot comment).
  let kindFilter: AssetKind | 'all' | 'template' = $state('all');
  // Full-project starters (replace everything, confirm-gated) vs. individual
  // layer templates (Orb/Ring/Streak/Spin Wheel/Ambient Beam/Shard — just
  // insert, no confirm needed) are different enough operations to need their
  // own sub-tab within the Templates category, per the original split plan.
  let templateScope: 'projects' | 'layers' = $state('projects');
  let renamingId: string | null = $state(null);
  let renameValue = $state('');

  const filtered = $derived(
    assetLibrary.assets.filter(
      (a) => (kindFilter === 'all' || a.kind === kindFilter) && a.name.toLowerCase().includes(query.toLowerCase())
    )
  );
  const filteredTemplates = $derived(TEMPLATES.filter((t) => t.label.toLowerCase().includes(query.toLowerCase())));
  const filteredLayerTemplates = $derived(LAYER_TEMPLATES.filter((t) => t.label.toLowerCase().includes(query.toLowerCase())));

  async function onLoadTemplate(id: string) {
    if (!store) return;
    const tpl = TEMPLATES.find((t) => t.id === id);
    if (tpl) await loadTemplateIntoStore(store, tpl);
  }

  function onAddLayerTemplate(id: string) {
    if (!store) return;
    const tpl = LAYER_TEMPLATES.find((t) => t.id === id);
    if (!tpl) return;
    store.addLayers(tpl.build(store.project.meta.songEnd));
    toast.push('success', `Added "${tpl.label}" layer`);
  }

  function onNewScript() {
    const entry = assetLibrary.addScript(`script-${assetLibrary.assets.filter((a) => a.kind === 'script').length + 1}`);
    kindFilter = 'script';
    uiState.editingScriptAssetId = entry.id;
  }

  function onCardActivate(a: (typeof assetLibrary.assets)[number]) {
    if (a.kind === 'script') uiState.editingScriptAssetId = a.id;
    else startRename(a.id, a.name);
  }

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
    <button class="small ghost" onclick={onNewScript}><ScrollText size={12} /> New Script</button>
    <span class="sep"></span>
    <div class="kindFilter">
      {#each [['all', 'All'], ['image', 'Image'], ['audio', 'Audio'], ['svg', 'SVG'], ['script', 'Scripts'], ['template', 'Templates']] as [id, label] (id)}
        <button class="small ghost" class:active={kindFilter === id} onclick={() => (kindFilter = id as AssetKind | 'all' | 'template')}>{label}</button>
      {/each}
    </div>
    <span class="sep"></span>
    <span class="spacer"></span>
    <div class="search">
      <Search size={12} />
      <input type="text" placeholder="filter…" bind:value={query} />
    </div>
  </div>
  {#if kindFilter === 'template'}
    <div class="templateScope">
      <button class="small ghost" class:active={templateScope === 'projects'} onclick={() => (templateScope = 'projects')}>Full Projects</button>
      <button class="small ghost" class:active={templateScope === 'layers'} onclick={() => (templateScope = 'layers')}>Layers</button>
    </div>
  {/if}
  <div class="grid">
    {#if kindFilter === 'template' && templateScope === 'projects'}
      {#if !filteredTemplates.length}
        <p class="empty">Nothing matches.</p>
      {:else}
        {#each filteredTemplates as t (t.id)}
          <button class="card templateCard" onclick={() => onLoadTemplate(t.id)} title={t.hint} disabled={!store}>
            <div class="thumb"><LayoutTemplate size={22} /></div>
            <span class="name">{t.label}</span>
            <span class="size">{t.hint}</span>
          </button>
        {/each}
      {/if}
    {:else if kindFilter === 'template' && templateScope === 'layers'}
      {#if !filteredLayerTemplates.length}
        <p class="empty">Nothing matches.</p>
      {:else}
        {#each filteredLayerTemplates as t (t.id)}
          <button class="card templateCard" onclick={() => onAddLayerTemplate(t.id)} title={`${t.hint} — adds a new layer, doesn't replace anything`} disabled={!store}>
            <div class="thumb"><LayoutTemplate size={22} /></div>
            <span class="name">{t.label}</span>
            <span class="size">{t.hint}</span>
          </button>
        {/each}
      {/if}
    {:else if !assetLibrary.assets.length}
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
            {:else if a.kind === 'script'}
              <ScrollText size={22} />
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
            <button
              class="name"
              ondblclick={() => onCardActivate(a)}
              title={a.kind === 'script' ? 'Double-click to edit' : 'Double-click to rename'}
            >
              {a.name}
            </button>
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
  .templateScope {
    display: flex;
    gap: 2px;
    padding: 4px 8px 0;
    flex-shrink: 0;
  }
  .templateScope button.active {
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
  .templateCard {
    background: none;
    color: inherit;
  }
  .templateCard .size {
    white-space: normal;
    text-align: center;
    line-height: 1.3;
  }
  .templateCard:disabled {
    opacity: 0.5;
    cursor: not-allowed;
  }
</style>
