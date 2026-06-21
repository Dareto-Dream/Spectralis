<script lang="ts">
  import type { ProjectStore } from '../state/project.svelte';
  import type { AudioState } from '../state/audio.svelte';
  import type { AssetsState } from '../state/assets.svelte';
  import { confirmDialog } from '../state/confirmModal.svelte';
  import { toast } from '../state/toast.svelte';
  import { downloadText } from '../lib/downloadText';
  import { migrateProject } from '../lib/migrate';
  import { TEMPLATES } from '../lib/templates';
  import { buildExportFiles } from '../export/exportAll';
  import { parseLRC, flattenWords } from '../core/lrc.js';
  import { fmtTime } from '../lib/fmtTime';
  import type { Aspect } from '../types/project';

  let { store, audio, assets }: { store: ProjectStore; audio: AudioState; assets: AssetsState } = $props();

  let projectFileInput: HTMLInputElement | undefined = $state();
  let lrcFileInput: HTMLInputElement | undefined = $state();
  let coverFileInput: HTMLInputElement | undefined = $state();
  let selectedTemplateId = $state(TEMPLATES[0].id);
  let exporting = $state(false);
  let sharedPlay = $state(false);

  async function confirmUnsavedIfNeeded(): Promise<boolean> {
    if (!store.history.hasUncommittedSinceLoad) return true;
    return confirmDialog({
      title: 'Discard unsaved changes?',
      body: 'Loading will replace the current project. This can still be undone with Ctrl+Z right up until you load something else.',
      confirmLabel: 'Discard & Load',
      danger: true,
    });
  }

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
    toast.push('success', 'Project saved');
  }

  async function onLoadProjectClick() {
    if (await confirmUnsavedIfNeeded()) projectFileInput?.click();
  }

  async function onProjectFileChosen(e: Event) {
    const input = e.target as HTMLInputElement;
    const file = input.files?.[0];
    input.value = '';
    if (!file) return;
    try {
      const parsed = migrateProject(JSON.parse(await file.text()));
      store.loadProject(parsed);
      toast.push('success', `Loaded ${file.name}`);
    } catch (err) {
      await confirmDialog({
        title: "Couldn't load that project",
        body: `This file isn't a valid Studio project — ${err instanceof Error ? err.message : String(err)}`,
        confirmLabel: 'OK',
      });
    }
  }

  async function onTemplateClick() {
    if (!(await confirmUnsavedIfNeeded())) return;
    const tpl = TEMPLATES.find((t) => t.id === selectedTemplateId);
    if (!tpl) return;
    store.loadProject(tpl.build());
    toast.push('success', `Loaded "${tpl.label}"`);
  }

  function onAudioFileChosen(e: Event) {
    const file = (e.target as HTMLInputElement).files?.[0];
    if (file) audio.loadFile(file);
  }

  async function onLrcFileChosen(e: Event) {
    const input = e.target as HTMLInputElement;
    const file = input.files?.[0];
    input.value = '';
    if (!file) return;
    const raw = await file.text();
    const nonBlankLines = raw.trim().split('\n').filter((l) => l.trim()).length;

    let lyricsLayer = store.project.layers.find((l) => l.type === 'lyrics');
    const keySet: Record<string, boolean> = {};
    if (lyricsLayer?.type === 'lyrics' && lyricsLayer.params.keyWords) {
      for (const w of lyricsLayer.params.keyWords.split(/[\s,]+/)) {
        if (w) keySet[w.toLowerCase()] = true;
      }
    }
    const lines = parseLRC(raw);
    const skipped = Math.max(0, nonBlankLines - lines.length);
    const words = flattenWords(lines, keySet);
    if (!words.length) {
      toast.push('error', 'No lyric lines recognized in that file');
      return;
    }
    if (!lyricsLayer) lyricsLayer = store.addLayer('lyrics');
    if (lyricsLayer.type === 'lyrics') lyricsLayer.params.words = words;
    store.project.importedLrcRaw = raw;
    store.commit();
    toast.push(
      'success',
      skipped > 0 ? `Imported ${words.length} words, skipped ${skipped} unrecognized lines` : `Imported ${words.length} words from ${file.name}`
    );
  }

  function onCoverFileChosen(e: Event) {
    const file = (e.target as HTMLInputElement).files?.[0];
    if (file) assets.loadCover(file);
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
    <button onclick={() => store.undo()} disabled={!store.history.canUndo} title="Undo (Ctrl+Z)">↶ Undo</button>
    <button onclick={() => store.redo()} disabled={!store.history.canRedo} title="Redo (Ctrl+Shift+Z)">↷ Redo</button>
  </div>

  <div class="group">
    <select bind:value={selectedTemplateId} title="Starter template">
      {#each TEMPLATES as t (t.id)}
        <option value={t.id}>{t.label}</option>
      {/each}
    </select>
    <button onclick={onTemplateClick}>Load Template</button>
    <button onclick={saveProject}>Save Project</button>
    <button onclick={onLoadProjectClick}>Load Project</button>
    <input bind:this={projectFileInput} type="file" accept="application/json" hidden onchange={onProjectFileChosen} />
  </div>

  <div class="group">
    <label class="fileBtn">
      Load Audio
      <input type="file" accept="audio/*" hidden onchange={onAudioFileChosen} />
    </label>
    {#if audio.loaded}<span class="ok" title="Audio loaded">♪</span>{/if}
    {#if audio.error}<span class="err">{audio.error}</span>{/if}
    <button onclick={() => lrcFileInput?.click()}>Import LRC…</button>
    <input bind:this={lrcFileInput} type="file" accept=".lrc,text/plain" hidden onchange={onLrcFileChosen} />
    <button onclick={() => coverFileInput?.click()}>Load Cover…</button>
    <input bind:this={coverFileInput} type="file" accept="image/*" hidden onchange={onCoverFileChosen} />
    {#if assets.coverImage}
      <img class="coverThumb" src={assets.coverImage.dataUrl} alt="Cover" />
      <button class="small ghost" onclick={() => assets.clearCover()} title="Remove cover">✕</button>
    {/if}
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
