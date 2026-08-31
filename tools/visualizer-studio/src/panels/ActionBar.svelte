<script lang="ts">
  // Slim strip between MenuBar and TopBar: the one thing this shell was
  // missing was any always-visible sign of which workspace you're in, or a
  // way to Save/Undo/Redo/Export without opening a menu. Godot's own top bar
  // mixes exactly these two concerns (scene tabs + play controls) in one row.
  import type { ProjectStore } from '../state/project.svelte';
  import type { AudioState } from '../state/audio.svelte';
  import type { AssetsState } from '../state/assets.svelte';
  import { saveProjectFile, saveProjectFileAs } from '../lib/projectSave';
  import { renderCapsule } from '../lib/exportRun.svelte';
  import { exportSettings } from '../state/exportSettings.svelte';
  import { appMode, type AppMode } from '../state/appMode.svelte';
  import { saveWorldFile, saveWorldFileAs, exportWorldHtml } from '../lib/worldSave';
  import { dockManager } from './dockManager.svelte';
  import { toolState } from '../state/toolState.svelte';
  import Clapperboard from '@lucide/svelte/icons/clapperboard';
  import Globe from '@lucide/svelte/icons/globe';
  import LayoutGrid from '@lucide/svelte/icons/layout-grid';
  import Eye from '@lucide/svelte/icons/eye';
  import BookOpen from '@lucide/svelte/icons/book-open';
  import Save from '@lucide/svelte/icons/save';
  import SavePen from '@lucide/svelte/icons/save-pen';
  import Undo2 from '@lucide/svelte/icons/undo-2';
  import Redo2 from '@lucide/svelte/icons/redo-2';
  import Package from '@lucide/svelte/icons/package';

  let { store, audio, assets }: { store: ProjectStore; audio: AudioState; assets: AssetsState } = $props();

  // Capsule and World are separate top-level modes now (state/appMode.svelte.ts),
  // each with their own DockviewLayout instance — this switch swaps which one
  // is mounted, it doesn't just focus a tab within a shared layout.
  const MODES: { id: AppMode; label: string; icon: typeof Clapperboard }[] = [
    { id: 'capsule', label: 'Capsule', icon: Clapperboard },
    { id: 'world', label: 'World', icon: Globe },
  ];

  // Capsule's 3 viewports (Workspace/Preview/Story) and World's 2
  // (Workspace/Preview) — "Preview" temporarily maximizes that one panel to
  // fill the whole dockview (no editing chrome around it, per the plan's
  // "watch your capsule, no editing features"); switching to Workspace/Story
  // exits that. This is a navigation convenience layered on dockview's own
  // panel system, not a second dockview per mode.
  type Viewport = 'workspace' | 'preview' | 'story';
  const capsuleViewport: Viewport = $derived(
    dockManager.activePanelId === 'preview' ? 'preview' : dockManager.activePanelId === 'story' ? 'story' : 'workspace'
  );
  const worldViewport: Exclude<Viewport, 'story'> = $derived(dockManager.activePanelId === 'preview' ? 'preview' : 'workspace');

  function goCapsuleViewport(v: Viewport) {
    if (v === 'preview') {
      dockManager.focusOrOpen('preview');
      dockManager.maximize('preview');
    } else {
      dockManager.exitMaximized();
      dockManager.focusOrOpen(v === 'story' ? 'story' : 'workspace');
    }
  }

  function goWorldViewport(v: Exclude<Viewport, 'story'>) {
    if (v === 'preview') {
      dockManager.focusOrOpen('preview');
      dockManager.maximize('preview');
    } else {
      dockManager.exitMaximized();
      dockManager.focusOrOpen('nodeGraph');
    }
  }
</script>

<div class="actionBar">
  <div class="modes">
    {#each MODES as m (m.id)}
      <button class="small ghost" class:active={appMode.mode === m.id} onclick={() => (appMode.mode = m.id)}>
        <m.icon size={12} />
        {m.label}
      </button>
    {/each}
  </div>
  <span class="sep"></span>
  {#if appMode.mode === 'capsule'}
    <div class="modes">
      <button class="small ghost" class:active={capsuleViewport === 'workspace'} onclick={() => goCapsuleViewport('workspace')}>
        <LayoutGrid size={12} /> Workspace
      </button>
      <button class="small ghost" class:active={capsuleViewport === 'preview'} onclick={() => goCapsuleViewport('preview')}>
        <Eye size={12} /> Preview
      </button>
      <button class="small ghost" class:active={capsuleViewport === 'story'} onclick={() => goCapsuleViewport('story')}>
        <BookOpen size={12} /> Story
      </button>
    </div>
  {:else}
    <div class="modes">
      <button class="small ghost" class:active={worldViewport === 'workspace'} onclick={() => goWorldViewport('workspace')}>
        <LayoutGrid size={12} /> Workspace
      </button>
      <button class="small ghost" class:active={worldViewport === 'preview'} onclick={() => goWorldViewport('preview')}>
        <Eye size={12} /> Preview
      </button>
    </div>
  {/if}
  {#if appMode.mode === 'capsule'}
    <span class="sep"></span>
    <!-- The active Workspace tool's own options (brush size/opacity/hardness,
         shape fill/stroke, pen/node hints) — deliberately here, not in the
         left Tools docker (ToolsPanel.svelte), which stays a fixed grid of
         icon buttons so it never resizes as you switch tools. -->
    <div class="toolOptions">
      {#if toolState.active === 'pen'}
        <input type="color" title="Fill" bind:value={toolState.fillColor} />
        <input type="color" title="Stroke" bind:value={toolState.strokeColor} />
        <label class="opt"><span>Width</span><input class="num" type="number" min="0" step="0.5" bind:value={toolState.strokeWidth} /></label>
        <span class="toolHint">Click to place anchors · drag while placing to pull a curve handle · click the start point to close</span>
        {#if toolState.editingShapeId}
          <button class="small ghost" onclick={() => toolState.finishPath()}>Done</button>
        {/if}
      {:else if toolState.active === 'node'}
        <span class="toolHint">Drag a point to move it · Alt-drag a point to pull out a curve handle · drag a handle to reshape</span>
        {#if toolState.editingShapeId}
          <button class="small ghost" onclick={() => toolState.finishPath()}>Done</button>
        {/if}
      {:else if toolState.active === 'brush'}
        <label class="opt"><span>Size</span><input type="range" min="2" max="120" bind:value={toolState.brushSize} /></label>
        <label class="opt"><span>Opacity</span><input type="range" min="0" max="1" step="0.05" bind:value={toolState.brushOpacity} /></label>
        <label class="opt"><span>Hardness</span><input type="range" min="0" max="1" step="0.05" bind:value={toolState.brushHardness} /></label>
        <input type="color" title="Brush color" bind:value={toolState.brushColor} />
      {:else if toolState.active === 'rect' || toolState.active === 'ellipse' || toolState.active === 'line' || toolState.active === 'polygon'}
        <input type="color" title="Fill" bind:value={toolState.fillColor} />
        <input type="color" title="Stroke" bind:value={toolState.strokeColor} />
        <label class="opt"><span>Width</span><input class="num" type="number" min="0" step="0.5" bind:value={toolState.strokeWidth} /></label>
      {:else}
        <span class="toolHint">Click a shape to select just it. Drag its body to move it; use the layer's own corner/rotate handles to transform the whole layer.</span>
      {/if}
    </div>
  {/if}
  <span class="spacer"></span>
  {#if appMode.mode === 'capsule'}
    <div class="actions">
      <button class="icon ghost" title="Save Project (Ctrl+S)" aria-label="Save Project" onclick={() => saveProjectFile(store, audio, assets)}>
        <Save size={14} />
      </button>
      <button class="icon ghost" title="Save Project As… (Ctrl+Shift+S)" aria-label="Save Project As" onclick={() => saveProjectFileAs(store, audio, assets)}>
        <SavePen size={14} />
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
  {:else}
    <div class="actions">
      <button class="icon ghost" title="Save World (.spectral) — Ctrl+S" aria-label="Save World" onclick={() => saveWorldFile()}>
        <Save size={14} />
      </button>
      <button class="icon ghost" title="Save World As… (Ctrl+Shift+S)" aria-label="Save World As" onclick={() => saveWorldFileAs()}>
        <SavePen size={14} />
      </button>
      <span class="sep"></span>
      <button class="small ghost" title="Export World to HTML" aria-label="Export World" onclick={() => exportWorldHtml()}>
        <Package size={12} /> Export
      </button>
    </div>
  {/if}
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
  .toolOptions {
    display: flex;
    align-items: center;
    gap: 8px;
    min-width: 0;
  }
  .toolHint {
    font: 10px var(--mono);
    color: var(--dim2);
    white-space: nowrap;
    overflow: hidden;
    text-overflow: ellipsis;
    max-width: 46vw;
  }
  .opt {
    display: flex;
    align-items: center;
    gap: 4px;
    font: 10px var(--mono);
    color: var(--dim);
    white-space: nowrap;
  }
  .opt input[type='range'] {
    width: 70px;
  }
  .num {
    width: 42px;
  }
</style>
