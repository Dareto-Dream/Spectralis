<script lang="ts">
  import type { ProjectStore } from '../state/project.svelte';
  import type { AudioState } from '../state/audio.svelte';
  import type { AssetsState } from '../state/assets.svelte';
  import { confirmDialog } from '../state/confirmModal.svelte';
  import { toast } from '../state/toast.svelte';
  import { autosave } from '../state/autosave.svelte';
  import { downloadText } from '../lib/downloadText';
  import { TEMPLATES } from '../lib/templates';
  import { buildExportFiles } from '../export/exportAll';
  import { fmtTime } from '../lib/fmtTime';
  import { dropZone } from '../lib/dropZone';
  import { importLrcFile } from '../lib/lrcImport';
  import { confirmUnsavedIfNeeded, importProjectFile } from '../lib/projectImport';
  import type { Aspect } from '../types/project';
  import Undo2 from '@lucide/svelte/icons/undo-2';
  import Redo2 from '@lucide/svelte/icons/redo-2';
  import Music from '@lucide/svelte/icons/music';
  import X from '@lucide/svelte/icons/x';

  let { store, audio, assets }: { store: ProjectStore; audio: AudioState; assets: AssetsState } = $props();

  let projectFileInput: HTMLInputElement | undefined = $state();
  let lrcFileInput: HTMLInputElement | undefined = $state();
  let coverFileInput: HTMLInputElement | undefined = $state();
  let selectedTemplateId = $state(TEMPLATES[0].id);
  let exporting = $state(false);
  let sharedPlay = $state(false);

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

  function saveProject() {
    downloadText(`${store.project.meta.slug || 'project'}.studio.json`, JSON.stringify(store.project, null, 2));
    autosave.clear();
    toast.push('success', 'Project saved');
  }

  async function onLoadProjectClick() {
    if (await confirmUnsavedIfNeeded(store)) projectFileInput?.click();
  }

  function onProjectFileChosen(e: Event) {
    const input = e.target as HTMLInputElement;
    const file = input.files?.[0];
    input.value = '';
    if (file) importProjectFile(store, file);
  }

  async function onTemplateClick() {
    if (!(await confirmUnsavedIfNeeded(store))) return;
    const tpl = TEMPLATES.find((t) => t.id === selectedTemplateId);
    if (!tpl) return;
    store.loadProject(tpl.build());
    toast.push('success', `Loaded "${tpl.label}"`);
  }

  function onAudioFileChosen(e: Event) {
    const file = (e.target as HTMLInputElement).files?.[0];
    if (file) audio.loadFile(file);
  }

  function onLrcFileChosen(e: Event) {
    const input = e.target as HTMLInputElement;
    const file = input.files?.[0];
    input.value = '';
    if (file) importLrcFile(store, file);
  }

  function onCoverFileChosen(e: Event) {
    const file = (e.target as HTMLInputElement).files?.[0];
    if (file) assets.loadCover(file);
  }

  // Drag-and-drop file intake (plan QoL §C) — the old tool has zero drop
  // handlers anywhere. The existing hidden-<input> + button path stays
  // primary (already keyboard/click accessible); drop is additive.
  function isAudioFile(f: File) {
    return f.type.startsWith('audio/') || /\.(mp3|wav|ogg|m4a|flac|aac)$/i.test(f.name);
  }
  function isLrcFile(f: File) {
    return f.name.toLowerCase().endsWith('.lrc') || f.type === 'text/plain';
  }
  function isImageFile(f: File) {
    return f.type.startsWith('image/');
  }
  function isProjectFile(f: File) {
    return f.name.toLowerCase().endsWith('.json');
  }

  // QoL export pre-flight: slug is a hard requirement (nothing to name the files
  // with); zero layers / no audio are warn-level confirms, not hard blocks — a
  // silent visualizer with no audio, or an export with no layers yet, are both
  // legal if unusual, so they get a nudge instead of being refused outright.
  async function preflightOk(): Promise<boolean> {
    if (!store.project.meta.slug) {
      toast.push('error', 'Set a slug in the top bar first.');
      return false;
    }
    if (store.project.layers.length === 0) {
      const ok = await confirmDialog({ title: 'Export with no layers?', body: 'This project has no layers yet — the exported visualizer will render nothing but the section background.', confirmLabel: 'Export anyway' });
      if (!ok) return false;
    }
    if (!audio.loaded) {
      const ok = await confirmDialog({ title: 'Export without audio?', body: 'No audio is loaded — the manifest\'s audio.sha256 will be a placeholder until you load a track and re-export.', confirmLabel: 'Export anyway' });
      if (!ok) return false;
    }
    return true;
  }

  async function doExport() {
    if (!(await preflightOk())) return;
    exporting = true;
    try {
      // buildVisualizerHtml et al are plain (non-Svelte) modules so they stay
      // testable without the runes machinery — structuredClone inside them can't
      // clone a live $state proxy directly, so snapshot to a plain object first.
      const project = $state.snapshot(store.project) as typeof store.project;
      const files = buildExportFiles({
        project,
        audioSha256: audio.sha256,
        coverExtension: assets.coverImage ? assets.extension : null,
        sharedPlay,
      });
      for (const file of files) downloadText(file.name, file.content);
      toast.push('success', `Exported ${files.length} files for ${store.project.meta.slug}`);
    } finally {
      exporting = false;
    }
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

  <div class="group">
    <button class="small ghost" onclick={() => store.undo()} disabled={!store.history.canUndo} title="Undo (Ctrl+Z)"><Undo2 size={12} /> Undo</button>
    <button class="small ghost" onclick={() => store.redo()} disabled={!store.history.canRedo} title="Redo (Ctrl+Shift+Z)"><Redo2 size={12} /> Redo</button>
  </div>

  <div
    class="group"
    title="Drop a .studio.json project file here to load it"
    use:dropZone={{
      accept: isProjectFile,
      onDrop: (f) => importProjectFile(store, f),
      onReject: () => toast.push('error', "That doesn't look like a Studio project (.json)"),
    }}
  >
    <select bind:value={selectedTemplateId} title="Starter template">
      {#each TEMPLATES as t (t.id)}
        <option value={t.id}>{t.label}</option>
      {/each}
    </select>
    <button onclick={onTemplateClick}>Load Template</button>
    <button data-action="save-project" onclick={saveProject}>Save Project</button>
    <button onclick={onLoadProjectClick}>Load Project</button>
    <input bind:this={projectFileInput} type="file" accept="application/json" hidden onchange={onProjectFileChosen} />
  </div>

  <div class="group">
    <label
      class="fileBtn"
      title="Click, or drop an audio file here"
      use:dropZone={{
        accept: isAudioFile,
        onDrop: (f) => audio.loadFile(f),
        onReject: () => toast.push('error', "That doesn't look like an audio file"),
      }}
    >
      Load Audio
      <input type="file" accept="audio/*" hidden onchange={onAudioFileChosen} />
    </label>
    {#if audio.loaded}<span class="ok" title="Audio loaded"><Music size={13} /></span>{/if}
    {#if audio.error}<span class="err">{audio.error}</span>{/if}
    <span
      class="dropWrap"
      title="Drop a .lrc lyrics file here"
      use:dropZone={{
        accept: isLrcFile,
        onDrop: (f) => importLrcFile(store, f),
        onReject: () => toast.push('error', "That doesn't look like an .lrc lyrics file"),
      }}
    >
      <button onclick={() => lrcFileInput?.click()}>Import LRC…</button>
    </span>
    <input bind:this={lrcFileInput} type="file" accept=".lrc,text/plain" hidden onchange={onLrcFileChosen} />
    <span
      class="dropWrap"
      title="Drop a cover image here"
      use:dropZone={{
        accept: isImageFile,
        onDrop: (f) => assets.loadCover(f),
        onReject: () => toast.push('error', "That doesn't look like an image file"),
      }}
    >
      <button onclick={() => coverFileInput?.click()}>Load Cover…</button>
      {#if assets.coverImage}
        <img class="coverThumb" src={assets.coverImage.dataUrl} alt="Cover" />
        <button class="icon ghost" onclick={() => assets.clearCover()} title="Remove cover" aria-label="Remove cover"><X size={12} /></button>
      {/if}
    </span>
    <input bind:this={coverFileInput} type="file" accept="image/*" hidden onchange={onCoverFileChosen} />
  </div>

  <label class="sharedPlay" title="Manifest capabilities include sharedPlay.* only when this is checked">
    <input type="checkbox" bind:checked={sharedPlay} /> Shared Play
  </label>

  <div class="spacer"></div>
  <span class="readout">{fmtTime(store.playhead)}</span>
  <button class="export" onclick={doExport} disabled={exporting}>{exporting ? 'Exporting…' : 'Export Capsule'}</button>
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
  .fileBtn {
    display: inline-flex;
    align-items: center;
    padding: 4px 8px;
    background: var(--bg3);
    border: 1px solid var(--line);
    border-radius: 3px;
    cursor: pointer;
  }
  .dropWrap {
    display: inline-flex;
    align-items: center;
    gap: 4px;
    border-radius: 3px;
  }
  .ok {
    color: var(--good);
  }
  .err {
    color: var(--danger);
    max-width: 200px;
    overflow: hidden;
    text-overflow: ellipsis;
    white-space: nowrap;
  }
  .coverThumb {
    width: 22px;
    height: 22px;
    object-fit: cover;
    border-radius: 3px;
    border: 1px solid var(--line);
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
  .export {
    background: var(--accent);
    color: #0a1420;
    font-weight: 600;
  }
</style>
