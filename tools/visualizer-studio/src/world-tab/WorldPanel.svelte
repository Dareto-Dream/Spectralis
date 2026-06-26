<script lang="ts">
  import { downloadText } from '../lib/downloadText';
  import { toast } from '../state/toast.svelte';
  import { buildWorldHtml, buildAlbumManifest, worldPreviewSrcdoc } from './buildWorldHtml';
  import type { AlbumMeta, WorldTrack } from '../types/world';
  import X from '@lucide/svelte/icons/x';

  let meta: AlbumMeta = $state({ albumId: 'my-album-2026', title: 'Album Title', artist: 'Artist Name', year: 2026, templateKind: 'tracklist' });
  let tracks: WorldTrack[] = $state([{ id: 'track-01', title: 'Track One', audio: 'tracks/01/audio.mp3', cover: 'tracks/01/cover.png', lrc: 'tracks/01/lyrics.lrc' }]);

  let outHtml = $state('');
  let outManifest = $state('');
  let previewSrcdoc = $state('');

  function addTrack() {
    const n = tracks.length + 1;
    const padded = n < 10 ? `0${n}` : String(n);
    tracks.push({ id: `track-${padded}`, title: `Track ${n}`, audio: `tracks/${padded}/audio.mp3`, cover: `tracks/${padded}/cover.png`, lrc: '' });
  }

  function removeTrack(i: number) {
    tracks.splice(i, 1);
  }

  function generate() {
    outHtml = buildWorldHtml(meta.templateKind, tracks);
    outManifest = buildAlbumManifest(meta, tracks);
    previewSrcdoc = worldPreviewSrcdoc(outHtml, meta.title, meta.artist);
    toast.push('success', 'World files generated — copy from the panels below');
  }

  async function copy(text: string, label: string) {
    await navigator.clipboard.writeText(text);
    toast.push('success', `Copied ${label}`);
  }

  function download() {
    downloadText('world_index.html', outHtml);
    downloadText(`${meta.albumId || 'album'}_manifest.json`, outManifest);
  }
</script>

<div class="builderGrid">
  <div class="col">
    <fieldset>
      <legend>Album</legend>
      <label class="field"><span>Album ID</span><input bind:value={meta.albumId} /></label>
      <label class="field"><span>Title</span><input bind:value={meta.title} /></label>
      <label class="field"><span>Artist</span><input bind:value={meta.artist} /></label>
      <label class="field"><span>Year</span><input type="number" bind:value={meta.year} /></label>
      <label class="field">
        <span>Template</span>
        <select bind:value={meta.templateKind}>
          <option value="tracklist">Tracklist card grid</option>
          <option value="levelmap">Level map (Mario-World style nodes)</option>
        </select>
      </label>
    </fieldset>
    <fieldset>
      <legend>Tracks</legend>
      {#each tracks as track, i (i)}
        <div class="trackRow">
          <input placeholder="id" bind:value={track.id} />
          <input placeholder="title" bind:value={track.title} />
          <input placeholder="audio path" bind:value={track.audio} />
          <input placeholder="cover path" bind:value={track.cover} />
          <input placeholder="lrc path" bind:value={track.lrc} />
          <button class="icon ghost" title="Remove track" aria-label={`Remove track ${i + 1}`} onclick={() => removeTrack(i)}><X size={13} /></button>
        </div>
      {/each}
      <button class="small" onclick={addTrack}>+ Add Track</button>
    </fieldset>
    <div class="exportList">
      <button class="primary small" onclick={generate}>Generate World Files</button>
      {#if outHtml}<button class="small" onclick={download}>Download Both</button>{/if}
    </div>
  </div>
  <div class="col">
    <h3>Preview (rendered offline — real build gets live window.spectral data)</h3>
    <iframe title="World preview" srcdoc={previewSrcdoc}></iframe>
    <h3>
      world/index.html
      {#if outHtml}<button class="small copyBtn" onclick={() => copy(outHtml, 'world/index.html')}>Copy</button>{/if}
    </h3>
    <textarea class="code" readonly value={outHtml}></textarea>
    <h3>
      manifest.json (album)
      {#if outManifest}<button class="small copyBtn" onclick={() => copy(outManifest, 'manifest.json')}>Copy</button>{/if}
    </h3>
    <textarea class="code short" readonly value={outManifest}></textarea>
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
  .field {
    display: flex;
    justify-content: space-between;
    align-items: center;
    gap: 6px;
    margin-bottom: 4px;
  }
  .field input,
  .field select {
    width: 160px;
  }
  .trackRow {
    display: flex;
    flex-wrap: wrap;
    gap: 4px;
    margin-bottom: 6px;
  }
  .trackRow input {
    min-width: 90px;
    flex: 1 1 90px;
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
  iframe {
    width: 100%;
    height: 200px;
    border: 1px solid var(--line);
    background: #000;
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
