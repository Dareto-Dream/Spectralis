<script lang="ts">
  import type { ProjectStore } from '../state/project.svelte';
  import { capsuleAutosave, worldAutosave } from '../state/autosave.svelte';
  import { toast } from '../state/toast.svelte';
  import { appMode } from '../state/appMode.svelte';
  import { nodeWorldStore } from '../state/nodeWorld.svelte';
  import type { CapsuleFile } from '../format/capsuleFile';
  import type { WorldFile } from '../format/worldFile';
  import type { SceneNode } from '../types/node';

  let { store }: { store: ProjectStore } = $props();

  let capsuleEntry: CapsuleFile | null = $state(null);
  let worldEntry: WorldFile | null = $state(null);
  let dismissed = $state(false);

  // Re-checks whichever mode's autosave slot is relevant whenever the mode
  // changes (and once on init) — decodeCapsuleFile/decodeWorldFile already
  // migrate the project on the way out, so there's nothing else to do here
  // but hand the result to the right store.
  $effect(() => {
    const mode = appMode.mode;
    dismissed = false;
    if (mode === 'capsule') void capsuleAutosave.readSaved().then((e) => (capsuleEntry = e));
    else void worldAutosave.readSaved().then((e) => (worldEntry = e));
  });

  function relTime(ms: number): string {
    const s = Math.round((Date.now() - ms) / 1000);
    if (s < 60) return `${s}s ago`;
    const m = Math.round(s / 60);
    if (m < 60) return `${m}m ago`;
    return `${Math.round(m / 60)}h ago`;
  }

  function restoreCapsule() {
    if (!capsuleEntry) return;
    try {
      store.loadProject(capsuleEntry.project);
      toast.push('success', 'Restored unsaved capsule session');
    } catch {
      toast.push('error', "Couldn't restore the autosaved session — it may be corrupted");
    }
    dismissed = true;
  }

  function restoreWorld() {
    if (!worldEntry) return;
    try {
      const graph = Array.isArray(worldEntry.nodeGraph) ? (worldEntry.nodeGraph as SceneNode[]) : [];
      nodeWorldStore.loadGraph(graph, { name: worldEntry.meta.name, author: worldEntry.meta.author });
      toast.push('success', 'Restored unsaved world session');
    } catch {
      toast.push('error', "Couldn't restore the autosaved session — it may be corrupted");
    }
    dismissed = true;
  }

  function discardCapsule() {
    void capsuleAutosave.clear();
    capsuleEntry = null;
    dismissed = true;
  }

  function discardWorld() {
    void worldAutosave.clear();
    worldEntry = null;
    dismissed = true;
  }
</script>

{#if appMode.mode === 'capsule'}
  {#if capsuleEntry && !dismissed}
    <div class="banner">
      <span>Restore unsaved capsule session from {relTime(capsuleEntry.meta.modifiedAt ?? capsuleEntry.meta.createdAt ?? 0)}?</span>
      <button class="small" onclick={restoreCapsule}>Restore</button>
      <button class="small ghost" onclick={discardCapsule}>Discard</button>
    </div>
  {/if}
{:else if worldEntry && !dismissed}
  <div class="banner">
    <span>Restore unsaved world session from {relTime(worldEntry.meta.modifiedAt ?? worldEntry.meta.createdAt ?? 0)}?</span>
    <button class="small" onclick={restoreWorld}>Restore</button>
    <button class="small ghost" onclick={discardWorld}>Discard</button>
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
