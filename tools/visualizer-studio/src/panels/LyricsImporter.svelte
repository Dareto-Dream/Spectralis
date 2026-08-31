<script lang="ts">
  // Replaces the old raw "Import LRC…" flow (lib/lrcImport.ts, removed — it
  // wrote straight onto a `lyrics`-kind layer that no longer exists, see
  // types/project.ts's LayerType doc). Parse -> choose split/reveal settings
  // -> preview -> Commit generates a whole GROUP of plain keyframed `vector`
  // text layers (lib/lyricsImport.ts's buildLyricsLayerGroup), individually
  // editable afterward like any other layer.
  import type { ProjectStore } from '../state/project.svelte';
  import { uiState } from '../state/uiState.svelte';
  import { toast } from '../state/toast.svelte';
  import { focusTrap } from '../lib/focusTrap';
  import { dropZone } from '../lib/dropZone';
  import { drawVectorLayer } from '../core/shapes.js';
  import {
    parseLyricsFile,
    buildLyricsLayerGroup,
    DEFAULT_LYRICS_SETTINGS,
    type LyricsImportSettings,
    type LyricsSplitMode,
    type LyricsRevealMode,
  } from '../lib/lyricsImport';
  import { vectorizeText } from '../lib/vectorize';

  let { store }: { store: ProjectStore } = $props();

  let fileName = $state('');
  let raw = $state('');
  let settings: LyricsImportSettings = $state({ ...DEFAULT_LYRICS_SETTINGS });
  let previewCanvas: HTMLCanvasElement | undefined = $state();
  let previewT = $state(0);

  const lines = $derived(raw ? parseLyricsFile(raw) : []);
  const songEnd = $derived(store.project.meta.songEnd);
  const chunkCount = $derived(
    settings.splitMode === 'line'
      ? lines.length
      : settings.splitMode === 'word'
        ? lines.reduce((n, l) => n + l.words.length, 0)
        : Math.ceil(lines.reduce((n, l) => n + l.words.length, 0) / Math.max(1, settings.phraseWordCount))
  );

  async function onFile(file: File) {
    fileName = file.name;
    raw = await file.text();
    previewT = 0;
    toast.push('success', `Parsed ${file.name}`);
  }

  function findActiveText(t: number): string {
    if (!lines.length) return '';
    // Same "chunk containing t" logic buildLyricsLayerGroup uses internally,
    // simplified for line-level preview only — good enough to sanity-check
    // position/size/hue before committing, not a full mode-accurate render.
    let active = lines[0];
    for (const line of lines) {
      if (line.time <= t) active = line;
      else break;
    }
    return active.words.map((w) => w.text).join(' ');
  }

  $effect(() => {
    const canvas = previewCanvas;
    if (!canvas) return;
    const ctx = canvas.getContext('2d');
    if (!ctx) return;
    const W = canvas.width, H = canvas.height;
    ctx.fillStyle = '#0a0a0d';
    ctx.fillRect(0, 0, W, H);
    const text = findActiveText(previewT) || 'preview';
    const shapes = vectorizeText({ text: text.toUpperCase(), fontSize: settings.fontSize * (W / 270), tracking: 2 });
    ctx.save();
    ctx.translate(settings.x * (W / 270), settings.y * (H / 480));
    drawVectorLayer(ctx, shapes, settings.baseHue, settings.highlightHue, 1, performance.now(), 0, previewT);
    ctx.restore();
  });

  function commit() {
    if (!lines.length) {
      toast.push('error', 'Nothing parsed yet — drop an .lrc file first');
      return;
    }
    const generated = buildLyricsLayerGroup(lines, settings, songEnd);
    store.addLayers(generated, 'Lyrics');
    toast.push('success', `Added ${generated.length} lyric layer${generated.length === 1 ? '' : 's'}`);
    close();
  }

  function close() {
    uiState.lyricsImporterOpen = false;
    raw = '';
    fileName = '';
  }

  function onKeydown(e: KeyboardEvent) {
    if (e.key === 'Escape') close();
  }

  const SPLIT_MODES: { id: LyricsSplitMode; label: string }[] = [
    { id: 'line', label: 'By line' },
    { id: 'word', label: 'By word' },
    { id: 'phrase', label: 'By phrase' },
  ];
  const REVEAL_MODES: { id: LyricsRevealMode; label: string }[] = [
    { id: 'wordByWord', label: 'Fade in/out per chunk' },
    { id: 'lineHighlight', label: 'Hue pulse per chunk' },
  ];
</script>

{#if uiState.lyricsImporterOpen}
  <!-- svelte-ignore a11y_click_events_have_key_events -->
  <!-- svelte-ignore a11y_no_static_element_interactions -->
  <div class="scrim" onclick={close}>
    <div class="modal" role="dialog" aria-modal="true" tabindex="-1" onclick={(e) => e.stopPropagation()} onkeydown={onKeydown} use:focusTrap>
      <div class="head">
        <h3>Lyrics Importer</h3>
        <span class="hint">Generates a group of plain, individually-keyframed vector text layers — nothing special after that.</span>
      </div>

      <div class="body">
        <div class="col">
          <p
            class="dropHint"
            use:dropZone={{
              accept: (f) => f.name.toLowerCase().endsWith('.lrc') || f.type === 'text/plain',
              onDrop: (f) => onFile(f),
              onReject: () => toast.push('error', "That doesn't look like an .lrc lyrics file"),
            }}
          >
            {fileName ? `${fileName} — ${lines.length} lines parsed, ${chunkCount} layer(s) will be created` : 'Drop an .lrc file here'}
          </p>

          <label class="field">
            <span>Split</span>
            <select bind:value={settings.splitMode}>
              {#each SPLIT_MODES as m (m.id)}
                <option value={m.id}>{m.label}</option>
              {/each}
            </select>
          </label>
          {#if settings.splitMode === 'phrase'}
            <label class="field">
              <span>Words per phrase</span>
              <input type="number" min="1" step="1" bind:value={settings.phraseWordCount} />
            </label>
          {/if}
          <label class="field">
            <span>Reveal</span>
            <select bind:value={settings.revealMode}>
              {#each REVEAL_MODES as m (m.id)}
                <option value={m.id}>{m.label}</option>
              {/each}
            </select>
          </label>
          <label class="field">
            <span>Position X</span>
            <input type="number" bind:value={settings.x} />
          </label>
          <label class="field">
            <span>Position Y</span>
            <input type="number" bind:value={settings.y} />
          </label>
          <label class="field">
            <span>Font size</span>
            <input type="number" min="8" bind:value={settings.fontSize} />
          </label>
          <label class="field">
            <span>Base hue</span>
            <input type="number" min="0" max="360" bind:value={settings.baseHue} />
          </label>
          {#if settings.revealMode === 'lineHighlight'}
            <label class="field">
              <span>Highlight hue</span>
              <input type="number" min="0" max="360" bind:value={settings.highlightHue} />
            </label>
          {/if}
        </div>

        <div class="col preview">
          <canvas bind:this={previewCanvas} width="270" height="200"></canvas>
          <label class="field">
            <span>Preview time</span>
            <input type="range" min="0" max={songEnd} step="0.1" bind:value={previewT} />
          </label>
        </div>
      </div>

      <div class="actions">
        <button class="small ghost" onclick={close}>Cancel</button>
        <button class="small primary" onclick={commit} disabled={!lines.length}>Commit</button>
      </div>
    </div>
  </div>
{/if}

<style>
  .scrim {
    position: fixed;
    inset: 0;
    background: rgba(0, 0, 0, 0.55);
    display: flex;
    align-items: center;
    justify-content: center;
    z-index: 3000;
  }
  .modal {
    width: min(640px, 92vw);
    max-height: 86vh;
    overflow-y: auto;
    display: flex;
    flex-direction: column;
    gap: 10px;
    background: var(--bg1);
    border: 1px solid var(--line2);
    border-radius: 6px;
    padding: 14px;
    box-shadow: 0 20px 60px rgba(0, 0, 0, 0.6);
  }
  .head {
    display: flex;
    flex-direction: column;
    gap: 2px;
  }
  h3 {
    margin: 0;
    font: 13px var(--mono);
    color: var(--text);
  }
  .hint {
    font: 10px var(--mono);
    color: var(--dim2);
  }
  .body {
    display: flex;
    gap: 14px;
  }
  .col {
    flex: 1;
    display: flex;
    flex-direction: column;
    gap: 6px;
    min-width: 0;
  }
  .dropHint {
    padding: 10px;
    border: 1px dashed var(--line2);
    border-radius: 4px;
    font: 11px var(--mono);
    color: var(--dim);
    text-align: center;
  }
  :global(.dropHint.dragOver) {
    border-color: var(--accent);
    color: var(--accent);
  }
  .field {
    display: flex;
    align-items: center;
    justify-content: space-between;
    gap: 8px;
    font: 11px var(--mono);
    color: var(--dim);
  }
  .field input,
  .field select {
    width: 130px;
  }
  .field input[type='range'] {
    width: 100%;
  }
  .preview canvas {
    width: 100%;
    aspect-ratio: 270 / 200;
    background: #0a0a0d;
    border: 1px solid var(--line);
    border-radius: 4px;
  }
  .actions {
    display: flex;
    justify-content: flex-end;
    gap: 6px;
  }
</style>
