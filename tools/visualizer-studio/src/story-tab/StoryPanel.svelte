<script lang="ts">
  import { storyStore } from '../state/story.svelte';
  import { assetLibrary } from '../state/assetLibrary.svelte';
  import { toast } from '../state/toast.svelte';
  import { assetDrop } from '../lib/dragAsset';
  import type { AssetEntry } from '../types/asset';
  import X from '@lucide/svelte/icons/x';
  import GripVertical from '@lucide/svelte/icons/grip-vertical';
  import Sparkles from '@lucide/svelte/icons/sparkles';

  // Advanced mode (plan: "there is an advanced mode making it look more like
  // the normal capsule workspace") restructures the page list into a
  // List + Inspector pair — same shape as Capsule's Layers+Inspector — with
  // a per-page hue accent field basic mode has no room for. It's an
  // authoring-only convenience for now (see StoryPage.hue's comment); the
  // exported HTML contract is unchanged either way.
  let advancedMode = $state(false);
  let selectedPageId: string | null = $state(null);
  const selectedPage = $derived(storyStore.pages.find((p) => p.id === selectedPageId) ?? null);

  async function copy(text: string, label: string) {
    await navigator.clipboard.writeText(text);
    toast.push('success', `Copied ${label}`);
  }

  let dragIndex: number | null = $state(null);
  function onDragStart(i: number) {
    dragIndex = i;
  }
  function onDragOver(e: DragEvent) {
    e.preventDefault();
  }
  function onDropRow(i: number) {
    if (dragIndex === null) return;
    storyStore.reorderPage(dragIndex, i);
    dragIndex = null;
  }

  function onPortraitAsset(entry: AssetEntry) {
    if (entry.kind !== 'image' && entry.kind !== 'svg') {
      toast.push('error', 'Drop an image or SVG asset onto the portrait field');
      return;
    }
    storyStore.meta.portraitKey = entry.name.replace(/\.[^.]+$/, '');
    storyStore.meta.portraitAssetId = entry.id;
  }
</script>

<div class="builderGrid" class:advanced={advancedMode}>
  <div class="col">
    <div class="modeToggle">
      <button class="small ghost" class:active={advancedMode} onclick={() => (advancedMode = !advancedMode)}>
        <Sparkles size={12} /> Advanced Mode
      </button>
    </div>
    <fieldset>
      <legend>Narrator</legend>
      <label class="field"><span>Name</span><input bind:value={storyStore.meta.name} /></label>
      <label class="field">
        <span>Portrait binding</span>
        <span class="assetField" use:assetDrop={{ onAsset: onPortraitAsset }} title="Or drag an image/SVG asset here">
          {#if storyStore.meta.portraitAssetId && assetLibrary.get(storyStore.meta.portraitAssetId)}
            <img class="thumb" src={assetLibrary.get(storyStore.meta.portraitAssetId)?.dataUrl} alt="" />
          {/if}
          <input bind:value={storyStore.meta.portraitKey} />
        </span>
      </label>
      <label class="field"><span>Typewriter ms/char</span><input type="number" bind:value={storyStore.meta.charMs} /></label>
      <label class="field"><span>Accent hue</span><input type="number" min="0" max="360" bind:value={storyStore.meta.hue} /></label>
    </fieldset>

    {#if !advancedMode}
      <fieldset>
        <legend>Pages <span class="hintInline">— drag rows to reorder</span></legend>
        {#each storyStore.pages as page, i (page.id)}
          <!-- svelte-ignore a11y_no_static_element_interactions -->
          <div class="pageRow" draggable="true" ondragstart={() => onDragStart(i)} ondragover={onDragOver} ondrop={() => onDropRow(i)}>
            <span class="grip" title="Drag to reorder"><GripVertical size={13} /></span>
            <input class="speaker" placeholder="Speaker (defaults to narrator)" bind:value={page.speaker} />
            <textarea placeholder="Page text" bind:value={page.text}></textarea>
            <button class="icon ghost danger" title="Remove page" aria-label={`Remove page ${i + 1}`} onclick={() => storyStore.removePage(i)}><X size={13} /></button>
          </div>
        {/each}
        <button class="small" onclick={() => storyStore.addPage()}>+ Add Page</button>
      </fieldset>
    {/if}

    <div class="exportList">
      <button class="primary small" onclick={() => storyStore.generate()}>Generate Story Files</button>
      {#if storyStore.outHtml}<button class="small" onclick={() => storyStore.download()}>Download Both</button>{/if}
    </div>
  </div>

  {#if advancedMode}
    <div class="col pageList">
      <h3>Pages</h3>
      {#each storyStore.pages as page, i (page.id)}
        <!-- svelte-ignore a11y_no_static_element_interactions -->
        <button
          class="pageListRow"
          class:selected={selectedPageId === page.id}
          draggable="true"
          ondragstart={() => onDragStart(i)}
          ondragover={onDragOver}
          ondrop={() => onDropRow(i)}
          onclick={() => (selectedPageId = page.id)}
        >
          <span class="grip"><GripVertical size={12} /></span>
          {#if page.hue !== undefined}<span class="hueDot" style="background: hsl({page.hue}, 70%, 55%)"></span>{/if}
          <span class="rowLabel">{i + 1}. {page.speaker || '(narrator)'} — {page.text.slice(0, 24) || '(empty)'}</span>
        </button>
      {/each}
      <button class="small" onclick={() => (selectedPageId = storyStore.addPage().id)}>+ Add Page</button>
    </div>

    <div class="col pageInspector">
      <h3>Page Inspector</h3>
      {#if !selectedPage}
        <p class="hintInline">Select a page on the left.</p>
      {:else}
        <label class="field stacked"><span>Speaker</span><input bind:value={selectedPage.speaker} placeholder="(defaults to narrator)" /></label>
        <label class="field stacked"><span>Text</span><textarea class="fullText" bind:value={selectedPage.text}></textarea></label>
        <label class="field">
          <span>Accent hue override</span>
          <input
            type="number"
            min="0"
            max="360"
            value={selectedPage.hue ?? ''}
            placeholder={String(storyStore.meta.hue)}
            oninput={(e) => {
              const v = (e.target as HTMLInputElement).value;
              selectedPage.hue = v === '' ? undefined : Math.max(0, Math.min(360, parseInt(v, 10) || 0));
            }}
          />
        </label>
        <button
          class="small ghost danger"
          onclick={() => {
            const idx = storyStore.pages.findIndex((p) => p.id === selectedPage!.id);
            if (idx >= 0) storyStore.removePage(idx);
            selectedPageId = null;
          }}
        >
          <X size={12} /> Delete Page
        </button>
      {/if}
    </div>
  {/if}

  <div class="col">
    <h3>Preview</h3>
    <div class="previewFrame">
      <iframe title="Story preview" srcdoc={storyStore.previewSrcdoc}></iframe>
    </div>
    <h3>
      story/index.html
      {#if storyStore.outHtml}<button class="small copyBtn" onclick={() => copy(storyStore.outHtml, 'story/index.html')}>Copy</button>{/if}
    </h3>
    <textarea class="code" readonly value={storyStore.outHtml}></textarea>
    <h3>
      manifest.json "story" block
      {#if storyStore.outManifest}<button class="small copyBtn" onclick={() => copy(storyStore.outManifest, 'story fragment')}>Copy</button>{/if}
    </h3>
    <textarea class="code short" readonly value={storyStore.outManifest}></textarea>
  </div>
</div>

<style>
  .builderGrid {
    display: grid;
    grid-template-columns: 340px 1fr;
    gap: 16px;
    padding: 12px;
    height: 100%;
    overflow-y: auto;
    color: var(--dim);
    font: 11px var(--mono);
  }
  .builderGrid.advanced {
    /* narrator/toggle | page list | page inspector | preview — same
       list+inspector+preview shape as Capsule's Layers/Inspector/Preview. */
    grid-template-columns: 260px 220px 260px 1fr;
  }
  .modeToggle {
    display: flex;
    margin-bottom: 8px;
  }
  .modeToggle button.active {
    background: var(--bg3);
    color: var(--accent);
  }
  .pageList,
  .pageInspector {
    background: var(--bg1);
    border: 1px solid var(--line);
    border-radius: 4px;
    padding: 8px;
    display: flex;
    flex-direction: column;
    gap: 4px;
    align-self: start;
  }
  .pageListRow {
    display: flex;
    align-items: center;
    gap: 5px;
    text-align: left;
    background: none;
    border: 1px solid transparent;
    border-radius: 3px;
    padding: 4px 6px;
    color: var(--dim);
    font: 10px var(--mono);
  }
  .pageListRow:hover {
    background: var(--bg2);
  }
  .pageListRow.selected {
    background: var(--bg3);
    border-color: var(--line2);
    color: var(--text);
  }
  .hueDot {
    width: 7px;
    height: 7px;
    border-radius: 50%;
    flex-shrink: 0;
  }
  .rowLabel {
    overflow: hidden;
    text-overflow: ellipsis;
    white-space: nowrap;
  }
  .field.stacked {
    flex-direction: column;
    align-items: stretch;
    gap: 3px;
  }
  .field.stacked input,
  .fullText {
    width: 100%;
  }
  .fullText {
    min-height: 100px;
    resize: vertical;
    font: 11px var(--mono);
  }
  fieldset {
    border: 1px solid var(--line);
    border-radius: 4px;
    margin-bottom: 10px;
    padding: 8px;
  }
  legend {
    padding: 0 4px;
    color: var(--dim2);
  }
  .hintInline {
    font-size: 9px;
    color: var(--dim2);
    text-transform: none;
  }
  .field {
    display: flex;
    justify-content: space-between;
    align-items: center;
    gap: 6px;
    margin-bottom: 4px;
  }
  .field input {
    width: 160px;
  }
  .assetField {
    display: inline-flex;
    align-items: center;
    gap: 4px;
    border-radius: 3px;
  }
  .thumb {
    width: 18px;
    height: 18px;
    object-fit: cover;
    border-radius: 2px;
    border: 1px solid var(--line);
    flex-shrink: 0;
  }
  .pageRow {
    display: flex;
    gap: 4px;
    margin-bottom: 6px;
    align-items: flex-start;
    padding: 3px;
    border-radius: 3px;
  }
  .pageRow:hover {
    background: var(--bg2);
  }
  .grip {
    display: inline-flex;
    color: var(--dim2);
    cursor: grab;
    flex-shrink: 0;
    margin-top: 4px;
  }
  .pageRow .speaker {
    width: 110px;
    flex-shrink: 0;
  }
  .pageRow textarea {
    flex: 1;
    min-height: 50px;
    resize: vertical;
    font: 11px var(--mono);
  }
  h3 {
    display: flex;
    align-items: center;
    gap: 8px;
    color: var(--text);
    font-size: 12px;
    margin: 10px 0 4px;
  }
  .copyBtn {
    font-size: 10px;
  }
  .previewFrame {
    width: 100%;
    aspect-ratio: 16 / 9;
    border: 1px solid var(--line);
    background: #000;
    max-height: 340px;
  }
  .previewFrame iframe {
    width: 100%;
    height: 100%;
    border: none;
  }
  textarea.code {
    width: 100%;
    height: 220px;
    background: var(--bg2);
    border: 1px solid var(--line);
    color: var(--text);
    font: 10px var(--mono);
  }
  textarea.code.short {
    height: 140px;
  }
</style>
