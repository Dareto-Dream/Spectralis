<script lang="ts">
  // Slim strip between MenuBar and TopBar: the one thing this shell was
  // missing was any always-visible sign of which workspace you're in, or a
  // way to Save/Undo/Redo/Export without opening a menu. Godot's own top bar
  // mixes exactly these two concerns (scene tabs + play controls) in one row.
  import type { ProjectStore } from '../state/project.svelte';
  import type { AudioState } from '../state/audio.svelte';
  import type { AssetsState } from '../state/assets.svelte';
  import { dockManager } from './dockManager.svelte';
  import { saveProjectFile } from '../lib/projectSave';
  import { renderCapsule } from '../lib/exportRun.svelte';
  import { exportSettings } from '../state/exportSettings.svelte';
  import Clapperboard from '@lucide/svelte/icons/clapperboard';
  import Globe from '@lucide/svelte/icons/globe';
  import BookOpen from '@lucide/svelte/icons/book-open';
  import Save from '@lucide/svelte/icons/save';
  import Undo2 from '@lucide/svelte/icons/undo-2';
  import Redo2 from '@lucide/svelte/icons/redo-2';
  import Package from '@lucide/svelte/icons/package';

  let { store, audio, assets }: { store: ProjectStore; audio: AudioState; assets: AssetsState } = $props();

  // World/Story dock as tabs stacked on Preview (see POSITION_CHAIN in
  // DockviewLayout.svelte) — picking one just brings it to front, same as
  // clicking its dockview tab. Anything else active (timeline, inspector,
  // svgmaker, etc.) still reads as "Studio", since that's the main workspace.
  const mode = $derived(
    dockManager.activePanelId === 'world' ? 'world' : dockManager.activePanelId === 'story' ? 'story' : 'studio'
  );

  const MODES = [
    { id: 'studio' as const, label: 'Studio', icon: Clapperboard, panel: 'preview' as const },
    { id: 'world' as const, label: 'World', icon: Globe, panel: 'world' as const },
    { id: 'story' as const, label: 'Story', icon: BookOpen, panel: 'story' as const },
  ];
</script>

<div class="actionBar">
  <div class="modes">
    {#each MODES as m (m.id)}
      <button class="small ghost" class:active={mode === m.id} onclick={() => dockManager.focusOrOpen(m.panel)}>
        <m.icon size={12} />
        {m.label}
      </button>
    {/each}
  </div>
  <span class="spacer"></span>
  <div class="actions">
    <button class="icon ghost" title="Save Project (Ctrl+S)" aria-label="Save Project" onclick={() => saveProjectFile(store)}>
      <Save size={14} />
    </button>
    <button class="icon ghost" title="Undo (Ctrl+Z)" aria-label="Undo" disabled={!store.history.canUndo} onclick={() => store.undo()}>
      <Undo2 size={14} />
    </button>
    <button class="icon ghost" title="Redo (Ctrl+Shift+Z)" aria-label="Redo" disabled={!store.history.canRedo} onclick={() => store.redo()}>
      <Redo2 size={14} />
    </button>
    <span class="sep"></span>
    <button
      class="small ghost"
      title="Export Capsule"
      aria-label="Export Capsule"
      disabled={exportSettings.exporting}
      onclick={() => renderCapsule(store, audio, assets)}
    >
      <Package size={12} /> Export
    </button>
  </div>
</div>

<style>
  .actionBar {
    display: flex;
    align-items: center;
    gap: 8px;
    padding: 4px 8px;
    background: var(--bg1);
    border-bottom: 1px solid var(--line);
  }
  .modes {
    display: flex;
    gap: 2px;
  }
  .modes button.active {
    background: var(--bg3);
    color: var(--accent);
  }
  .spacer {
    flex: 1;
  }
  .actions {
    display: flex;
    align-items: center;
    gap: 2px;
  }
  .sep {
    width: 1px;
    align-self: stretch;
    background: var(--line);
    margin: 0 4px;
  }
</style>
