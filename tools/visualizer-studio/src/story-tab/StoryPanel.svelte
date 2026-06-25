<script lang="ts">
  import { downloadText } from '../lib/downloadText';
  import { toast } from '../state/toast.svelte';
  import { buildStoryHtml } from './buildStoryHtml';
  import { buildStoryManifestFragment } from './buildStoryManifestFragment';
  import type { StoryMeta, StoryPage } from '../types/story';

  let meta: StoryMeta = $state({ name: 'Narrator', portraitKey: 'portrait', charMs: 22, hue: 225 });
  let pages: StoryPage[] = $state([{ speaker: '', text: 'Something happened before this song started...' }]);

  let outHtml = $state('');
  let outManifest = $state('');
  let previewSrcdoc = $state('');

  function addPage() {
    pages.push({ speaker: '', text: '' });
  }
  function removePage(i: number) {
    pages.splice(i, 1);
  }

  function generate() {
    outHtml = buildStoryHtml(meta, pages);
    outManifest = buildStoryManifestFragment(meta, pages);
    previewSrcdoc = outHtml.replace('<' + '/script>', ';window.spectral=window.spectral||{resume:function(){}};<' + '/script>');
    toast.push('success', 'Story files generated');
  }

  async function copy(text: string, label: string) {
    await navigator.clipboard.writeText(text);
    toast.push('success', `Copied ${label}`);
  }

  function download() {
    downloadText('story_index.html', outHtml);
    downloadText('story_manifest_fragment.json', outManifest);
  }
</script>

<div class="builderGrid">
  <div class="col">
    <fieldset>
      <legend>Narrator</legend>
      <label class="field"><span>Name</span><input bind:value={meta.name} /></label>
      <label class="field"><span>Portrait binding</span><input bind:value={meta.portraitKey} /></label>
      <label class="field"><span>Typewriter ms/char</span><input type="number" bind:value={meta.charMs} /></label>
      <label class="field"><span>Accent hue</span><input type="number" min="0" max="360" bind:value={meta.hue} /></label>
    </fieldset>
    <fieldset>
      <legend>Pages</legend>
      {#each pages as page, i (i)}
        <div class="pageRow">
          <input class="speaker" placeholder="Speaker (defaults to narrator)" bind:value={page.speaker} />
          <textarea placeholder="Page text" bind:value={page.text}></textarea>
          <button class="small ghost" title="Remove page" aria-label={`Remove page ${i + 1}`} onclick={() => removePage(i)}>✕</button>
        </div>
      {/each}
      <button class="small" onclick={addPage}>+ Add Page</button>
    </fieldset>
    <div class="exportList">
      <button class="primary small" onclick={generate}>Generate Story Files</button>
      {#if outHtml}<button class="small" onclick={download}>Download Both</button>{/if}
    </div>
  </div>
  <div class="col">
    <h3>Preview</h3>
    <iframe title="Story preview" srcdoc={previewSrcdoc}></iframe>
    <h3>
      story/index.html
      {#if outHtml}<button class="small copyBtn" onclick={() => copy(outHtml, 'story/index.html')}>Copy</button>{/if}
    </h3>
    <textarea class="code" readonly value={outHtml}></textarea>
    <h3>
      manifest.json "story" block
      {#if outManifest}<button class="small copyBtn" onclick={() => copy(outManifest, 'story fragment')}>Copy</button>{/if}
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
  .field input {
    width: 160px;
  }
  .pageRow {
    display: flex;
    gap: 4px;
    margin-bottom: 6px;
    align-items: flex-start;
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
