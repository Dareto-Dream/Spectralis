<script lang="ts">
  import type { ProjectStore } from '../state/project.svelte';
  import { autosave } from '../state/autosave.svelte';
  import { migrateProject } from '../lib/migrate';
  import { toast } from '../state/toast.svelte';

  let { store }: { store: ProjectStore } = $props();

  let entry = $state(autosave.readSaved());
  let dismissed = $state(false);

  function relTime(ms: number): string {
    const s = Math.round((Date.now() - ms) / 1000);
    if (s < 60) return `${s}s ago`;
    const m = Math.round(s / 60);
    if (m < 60) return `${m}m ago`;
    return `${Math.round(m / 60)}h ago`;
  }

  function restore() {
    if (!entry) return;
    try {
      store.loadProject(migrateProject(entry.project));
      toast.push('success', 'Restored unsaved session');
    } catch {
      toast.push('error', "Couldn't restore the autosaved session — it may be corrupted");
    }
    dismissed = true;
  }

  function discard() {
    autosave.clear();
    dismissed = true;
  }
</script>

{#if entry && !dismissed}
  <div class="banner">
    <span>Restore unsaved session from {relTime(entry.savedAt)}?</span>
    <button class="small" onclick={restore}>Restore</button>
    <button class="small ghost" onclick={discard}>Discard</button>
  </div>
{/if}

<style>
  .banner {
    display: flex;
    align-items: center;
    gap: 10px;
    padding: 6px 10px;
    background: var(--bg3);
    border-bottom: 1px solid var(--accent);
    font: 11px var(--mono);
    color: var(--text);
  }
</style>
