<script lang="ts">
  import { worldStore } from '../state/world.svelte';
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
    worldStore.reorderTrack(dragIndex, i);
    dragIndex = null;
  }

  // Godot-style asset swapping: drag an asset card in from the Assets docker
  // onto a track's cover/audio field instead of hand-typing a bundle path.
  function onCoverAsset(i: number, entry: AssetEntry) {
    if (entry.kind !== 'image' && entry.kind !== 'svg') {
      toast.push('error', 'Drop an image or SVG asset onto the cover field');
      return;
    }
    worldStore.tracks[i].cover = `tracks/${worldStore.tracks[i].id}/${entry.name}`;
    worldStore.tracks[i].coverAssetId = entry.id;
  }
  function onAudioAsset(i: number, entry: AssetEntry) {
    if (entry.kind !== 'audio') {
      toast.push('error', 'Drop an audio asset onto the audio field');
      return;
    }
    worldStore.tracks[i].audio = `tracks/${worldStore.tracks[i].id}/${entry.name}`;
  }
</script>

<div class="builderGrid">
  <div class="col">
    <fieldset>
      <legend>Album</legend>
      <label class="field"><span>Album ID</span><input bind:value={worldStore.meta.albumId} /></label>
      <label class="field"><span>Title</span><input bind:value={worldStore.meta.title} /></label>
      <label class="field"><span>Artist</span><input bind:value={worldStore.meta.artist} /></label>
      <label class="field"><span>Year</span><input type="number" bind:value={worldStore.meta.year} /></label>
      <label class="field">
        <span>Template</span>
        <select bind:value={worldStore.meta.templateKind}>
          <option value="tracklist">Tracklist card grid</option>
          <option value="levelmap">Level map (Mario-World style nodes)</option>
        </select>
      </label>
    </fieldset>
    <fieldset>
      <legend>Tracks <span class="hintInline">— drag rows to reorder, drag assets from the Assets docker onto cover/audio</span></legend>
      {#each worldStore.tracks as track, i (track.id)}
        <!-- svelte-ignore a11y_no_static_element_interactions -->
        <div class="trackRow" draggable="true" ondragstart={() => onDragStart(i)} ondragover={onDragOver} ondrop={() => onDropRow(i)}>
          <span class="grip" title="Drag to reorder"><GripVertical size={13} /></span>
          <input class="idField" placeholder="id" bind:value={track.id} />
          <input class="titleField" placeholder="title" bind:value={track.title} />
          <span
            class="assetField"
            use:assetDrop={{ onAsset: (entry) => onAudioAsset(i, entry) }}
            title="audio path — or drag an audio asset here"
          >
            <input placeholder="audio path" bind:value={track.audio} />
          </span>
          <span
            class="assetField cover"
            use:assetDrop={{ onAsset: (entry) => onCoverAsset(i, entry) }}
            title="cover path — or drag an image asset here"
          >
            {#if track.coverAssetId && assetLibrary.get(track.coverAssetId)}
              <img class="coverThumb" src={assetLibrary.get(track.coverAssetId)?.dataUrl} alt="" />
            {/if}
            <input placeholder="cover path" bind:value={track.cover} />
          </span>
          <input placeholder="lrc path" bind:value={track.lrc} />
          <button class="icon ghost danger" title="Remove track" aria-label={`Remove track ${i + 1}`} onclick={() => worldStore.removeTrack(i)}><X size={13} /></button>
        </div>
      {/each}
      <button class="small" onclick={() => worldStore.addTrack()}>+ Add Track</button>
    </fieldset>
    <div class="exportList">
      <button class="primary small" onclick={() => worldStore.generate()}>Generate World Files</button>
      {#if worldStore.outHtml}<button class="small" onclick={() => worldStore.download()}>Download Both</button>{/if}
    </div>
  </div>
  <div class="col">
    <h3>Preview (rendered offline — real build gets live window.spectral data)</h3>
    <div class="previewFrame">
      <iframe title="World preview" srcdoc={worldStore.previewSrcdoc}></iframe>
    </div>
    <h3>
      world/index.html
      {#if worldStore.outHtml}<button class="small copyBtn" onclick={() => copy(worldStore.outHtml, 'world/index.html')}>Copy</button>{/if}
    </h3>
    <textarea class="code" readonly value={worldStore.outHtml}></textarea>
    <h3>
      manifest.json (album)
      {#if worldStore.outManifest}<button class="small copyBtn" onclick={() => copy(worldStore.outManifest, 'manifest.json')}>Copy</button>{/if}
    </h3>
    <textarea class="code short" readonly value={worldStore.outManifest}></textarea>
  </div>
</div>

<style>
  .builderGrid {
    display: grid;
    grid-template-columns: 380px 1fr;
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
    letter-spacing: 0;
  }
  .field {
    display: flex;
    justify-content: space-between;
    align-items: center;
    gap: 6px;
    margin-bottom: 4px;
  }
  .field input,
  .field select {
    width: 200px;
  }
  .trackRow {
    display: flex;
    flex-wrap: wrap;
    gap: 4px;
    margin-bottom: 6px;
    align-items: center;
    padding: 3px;
    border-radius: 3px;
  }
  .trackRow:hover {
    background: var(--bg2);
  }
  .grip {
    display: inline-flex;
    color: var(--dim2);
    cursor: grab;
    flex-shrink: 0;
  }
  .idField {
    width: 70px;
    flex-shrink: 0;
  }
  .titleField {
    min-width: 90px;
    flex: 1 1 90px;
  }
  .assetField {
    display: inline-flex;
    align-items: center;
    gap: 4px;
    border-radius: 3px;
  }
  .assetField input {
    min-width: 90px;
    flex: 1 1 90px;
  }
  .coverThumb {
    width: 18px;
    height: 18px;
    object-fit: cover;
    border-radius: 2px;
    border: 1px solid var(--line);
    flex-shrink: 0;
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
