<script lang="ts">
  import type { ProjectStore } from '../state/project.svelte';
  import { confirmDialog } from '../state/confirmModal.svelte';

  let { store }: { store: ProjectStore } = $props();

  const section = $derived(store.project.sections.find((s) => s.id === store.selection.sectionId) ?? null);

  function num(field: 'start' | 'end' | 'hue' | 'hue2' | 'intensity', e: Event) {
    if (!section) return;
    const v = parseFloat((e.target as HTMLInputElement).value);
    if (isNaN(v)) return;
    store.updateSection(section.id, { [field]: v });
  }

  async function onDelete() {
    if (!section) return;
    if (store.project.sections.length <= 1) return;
    const ok = await confirmDialog({
      title: 'Delete section?',
      body: `Delete "${section.label}"? You can undo this with Ctrl+Z.`,
      confirmLabel: 'Delete',
      danger: true,
    });
    if (ok) {
      store.deleteSection(section.id);
      store.selection.selectSection(null);
    }
  }
</script>

{#if section}
  <div class="sectionEditor">
    <label class="field"><span>Label</span><input value={section.label} onchange={(e) => store.updateSection(section.id, { label: (e.target as HTMLInputElement).value })} /></label>
    <label class="field"><span>Start</span><input type="number" step="0.1" value={section.start} onchange={(e) => num('start', e)} /></label>
    <label class="field"><span>End</span><input type="number" step="0.1" value={section.end} onchange={(e) => num('end', e)} /></label>
    <label class="field"><span>Hue</span><input type="number" step="1" value={section.hue} onchange={(e) => num('hue', e)} /></label>
    <label class="field"><span>Hue 2</span><input type="number" step="1" value={section.hue2} onchange={(e) => num('hue2', e)} /></label>
    <label class="field"><span>Intensity</span><input type="number" step="0.05" min="0" max="1" value={section.intensity} onchange={(e) => num('intensity', e)} /></label>
    <label class="checkField" title="Reference-style: no lyrics render during this section">
      <input type="checkbox" checked={section.noLyrics} onchange={(e) => store.updateSection(section.id, { noLyrics: (e.target as HTMLInputElement).checked })} />
      Suppress lyrics
    </label>
    <button class="danger" onclick={onDelete}>Delete Section</button>
  </div>
{/if}

<style>
  .sectionEditor {
    display: flex;
    align-items: center;
    gap: 10px;
    flex-wrap: wrap;
    padding: 6px 8px;
    background: var(--bg2);
    border-top: 1px solid var(--line);
    font: 11px var(--mono);
    color: var(--dim);
  }
  .field {
    display: flex;
    align-items: center;
    gap: 4px;
  }
  .field input[type='number'] {
    width: 55px;
  }
  .checkField {
    display: flex;
    align-items: center;
    gap: 4px;
  }
  .danger {
    margin-left: auto;
    color: var(--danger);
  }
</style>
