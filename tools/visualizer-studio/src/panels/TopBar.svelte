<script lang="ts">
  import type { ProjectStore } from '../state/project.svelte';
  import type { AudioState } from '../state/audio.svelte';
  import type { AssetsState } from '../state/assets.svelte';
  import { toast } from '../state/toast.svelte';
  import { fmtTime } from '../lib/fmtTime';
  import { dropZone } from '../lib/dropZone';
  import { assetDrop } from '../lib/dragAsset';
  import { exportSettings } from '../state/exportSettings.svelte';
  import type { Aspect } from '../types/project';
  import type { AssetEntry } from '../types/asset';
  import Music from '@lucide/svelte/icons/music';
  import X from '@lucide/svelte/icons/x';

  // Everything that used to be a row of action buttons here (Load/Save
  // Project, Load Template, Undo/Redo, Export) now lives in the File/Edit/
  // Tools menus — this strip is project PROPERTIES (data fields), not
  // commands, so it stays visible regardless of which dockers are open.
  let { store, audio, assets }: { store: ProjectStore; audio: AudioState; assets: AssetsState } = $props();

  function onSlugInput(e: Event) {
    const el = e.target as HTMLInputElement;
    const cleaned = el.value.replace(/[^a-z0-9\-_]/gi, '-');
    if (cleaned !== el.value) el.value = cleaned;
    store.updateMeta({ slug: cleaned });
  }

  function onSongEndChange(e: Event) {
    const v = parseFloat((e.target as HTMLInputElement).value);
    if (!isNaN(v) && v > 0) store.updateMeta({ songEnd: v });
  }

  function isAudioFile(f: File) {
    return f.type.startsWith('audio/') || /\.(mp3|wav|ogg|m4a|flac|aac)$/i.test(f.name);
  }
  function isImageFile(f: File) {
    return f.type.startsWith('image/');
  }

  function onCoverAsset(entry: AssetEntry) {
    if (entry.kind !== 'image' && entry.kind !== 'svg') {
      toast.push('error', 'Drop an image or SVG asset onto the cover slot');
      return;
    }
    assets.setCoverFromAsset(entry.name, entry.dataUrl);
  }
</script>

<div class="topbar">
  <div class="group">
    <input class="title" placeholder="Title" value={store.project.meta.title} onchange={(e) => store.updateMeta({ title: (e.target as HTMLInputElement).value })} />
    <input class="artist" placeholder="Artist" value={store.project.meta.artist} onchange={(e) => store.updateMeta({ artist: (e.target as HTMLInputElement).value })} />
    <input class="slug" placeholder="slug" value={store.project.meta.slug} oninput={onSlugInput} />
    <label class="field"><span>End</span><input type="number" step="0.5" value={store.project.meta.songEnd} onchange={onSongEndChange} /></label>
    <select value={store.project.meta.aspect} onchange={(e) => store.updateMeta({ aspect: (e.target as HTMLSelectElement).value as Aspect })}>
      <option value="9x16">9:16 portrait</option>
      <option value="16x9">16:9 landscape</option>
      <option value="1x1">1:1 square</option>
    </select>
  </div>

  <div
    class="group"
    title="Drop an audio file here to load it"
    use:dropZone={{
      accept: isAudioFile,
      onDrop: (f) => audio.loadFile(f),
      onReject: () => toast.push('error', "That doesn't look like an audio file"),
    }}
  >
    <span class="audioStatus" class:ok={audio.loaded}>
      <Music size={13} />
      {audio.loaded ? (audio.file?.name ?? 'Audio loaded') : 'No audio (drop one here, or File > Import)'}
    </span>
    {#if audio.error}<span class="err">{audio.error}</span>{/if}
  </div>

  <div
    class="group coverGroup"
    title="Drop a cover image here — or drag an asset in from the Assets docker"
    use:dropZone={{ accept: isImageFile, onDrop: (f) => assets.loadCover(f), onReject: () => toast.push('error', "That doesn't look like an image file") }}
    use:assetDrop={{ onAsset: onCoverAsset }}
  >
    {#if assets.coverImage}
      <img class="coverThumb" src={assets.coverImage.dataUrl} alt="Cover" />
      <button class="icon ghost" onclick={() => assets.clearCover()} title="Remove cover" aria-label="Remove cover"><X size={12} /></button>
    {:else}
      <span class="coverPlaceholder">No cover</span>
    {/if}
  </div>

  <label class="sharedPlay" title="Manifest capabilities include sharedPlay.* only when this is checked">
    <input type="checkbox" bind:checked={exportSettings.sharedPlay} /> Shared Play
  </label>

  <div class="spacer"></div>
  <span class="readout">{fmtTime(store.playhead)}</span>
</div>

<style>
  .topbar {
    display: flex;
    align-items: center;
    gap: 14px;
    padding: 6px 10px;
    background: var(--bg1);
    border-bottom: 1px solid var(--line);
    font: 11px var(--mono);
    color: var(--dim);
    flex-wrap: wrap;
  }
  .group {
    display: flex;
    align-items: center;
    gap: 4px;
    border-radius: 3px;
  }
  .field {
    display: flex;
    align-items: center;
    gap: 3px;
  }
  .title {
    width: 120px;
  }
  .artist {
    width: 90px;
  }
  .slug {
    width: 90px;
  }
  input[type='number'] {
    width: 60px;
  }
  .audioStatus {
    display: inline-flex;
    align-items: center;
    gap: 5px;
    color: var(--dim2);
    padding: 4px 6px;
    border-radius: 3px;
  }
  .audioStatus.ok {
    color: var(--good);
  }
  .err {
    color: var(--danger);
    max-width: 200px;
    overflow: hidden;
    text-overflow: ellipsis;
    white-space: nowrap;
  }
  .coverGroup {
    min-width: 32px;
    min-height: 24px;
    padding: 2px 4px;
    border: 1px dashed var(--line);
  }
  .coverThumb {
    width: 22px;
    height: 22px;
    object-fit: cover;
    border-radius: 3px;
    border: 1px solid var(--line);
  }
  .coverPlaceholder {
    color: var(--dim2);
    font-size: 10px;
    padding: 0 4px;
  }
  .sharedPlay {
    display: flex;
    align-items: center;
    gap: 4px;
  }
  .spacer {
    flex: 1;
  }
  .readout {
    font-variant-numeric: tabular-nums;
  }
</style>
