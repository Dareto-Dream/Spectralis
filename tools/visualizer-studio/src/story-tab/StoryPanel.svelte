<script lang="ts">
  import { storyStore } from '../state/story.svelte';
  import { assetLibrary } from '../state/assetLibrary.svelte';
  import { toast } from '../state/toast.svelte';
  import { assetDrop } from '../lib/dragAsset';
  import type { AssetEntry } from '../types/asset';
  import X from '@lucide/svelte/icons/x';
  import GripVertical from '@lucide/svelte/icons/grip-vertical';

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

<div class="builderGrid">
  <div class="col">
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
    <div class="exportList">
      <button class="primary small" onclick={() => storyStore.generate()}>Generate Story Files</button>
      {#if storyStore.outHtml}<button class="small" onclick={() => storyStore.download()}>Download Both</button>{/if}
    </div>
  </div>
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
