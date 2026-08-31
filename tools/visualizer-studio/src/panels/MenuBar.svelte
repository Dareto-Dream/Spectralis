<script lang="ts">
  import { tick } from 'svelte';
  import type { ProjectStore } from '../state/project.svelte';
  import type { AudioState } from '../state/audio.svelte';
  import type { AssetsState } from '../state/assets.svelte';
  import type { TopMenu } from '../lib/menuTypes';
  import MenuList from './MenuList.svelte';
  import { TEMPLATES } from '../lib/templates';
  import { confirmUnsavedIfNeeded, importProjectFile, loadTemplateIntoStore } from '../lib/projectImport';
  import { saveProjectFile, saveProjectFileAs } from '../lib/projectSave';
  import { renderCapsule } from '../lib/exportRun.svelte';
  import { deleteSelection } from '../lib/deleteSelection';
  import { dockManager, DOCK_PANEL_TITLES, type DockPanelId } from './dockManager.svelte';
  import { worldStore } from '../state/world.svelte';
  import { storyStore } from '../state/story.svelte';
  import { uiState } from '../state/uiState.svelte';
  import { toast } from '../state/toast.svelte';
  import { appMode } from '../state/appMode.svelte';
  import { saveWorldFile, saveWorldFileAs, loadWorldFile } from '../lib/worldSave';
  import { loadAudioFile } from '../lib/audioLoad';

  let { store, audio, assets }: { store: ProjectStore; audio: AudioState; assets: AssetsState } = $props();

  let projectFileInput: HTMLInputElement | undefined = $state();
  let audioFileInput: HTMLInputElement | undefined = $state();
  let coverFileInput: HTMLInputElement | undefined = $state();
  let worldFileInput: HTMLInputElement | undefined = $state();

  function onWorldFileChosen(e: Event) {
    const input = e.target as HTMLInputElement;
    const file = input.files?.[0];
    input.value = '';
    if (file) loadWorldFile(file);
  }

  async function newProject() {
    if (!(await confirmUnsavedIfNeeded(store))) return;
    store.newProject();
    toast.push('success', 'New project');
  }

  async function loadTemplate(id: string) {
    const tpl = TEMPLATES.find((t) => t.id === id);
    if (tpl) await loadTemplateIntoStore(store, tpl);
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
  function onAudioFileChosen(e: Event) {
    const file = (e.target as HTMLInputElement).files?.[0];
    (e.target as HTMLInputElement).value = '';
    if (file) loadAudioFile(store, audio, file);
  }
  function onCoverFileChosen(e: Event) {
    const input = e.target as HTMLInputElement;
    const file = input.files?.[0];
    input.value = '';
    if (file) assets.loadCover(file);
  }

  // Dockers list is scoped to whichever mode's dockview is actually live
  // (DockviewLayout.svelte builds a different panel set per mode) — checking
  // a panel that belongs to the other mode would just add a dead, blank tab.
  const CAPSULE_DOCKERS: DockPanelId[] = ['tools', 'workspace', 'preview', 'timeline', 'inspector', 'layers', 'assets', 'story', 'script', 'svgmaker'];
  const WORLD_DOCKERS: DockPanelId[] = ['nodeGraph', 'nodeInspector', 'spriteEditor', 'preview', 'world'];
  const dockers = $derived(appMode.mode === 'world' ? WORLD_DOCKERS : CAPSULE_DOCKERS);

  const menus = $derived.by((): TopMenu[] => [
    {
      label: 'File',
      items: [
        { kind: 'action', label: 'New Project', action: newProject },
        {
          kind: 'submenu',
          label: 'Load Template',
          items: TEMPLATES.map((t) => ({ kind: 'action' as const, label: t.label, action: () => loadTemplate(t.id) })),
        },
        { kind: 'action', label: 'Load Project…', action: onLoadProjectClick },
        { kind: 'action', label: 'Save Project', shortcut: 'Ctrl+S', action: () => saveProjectFile(store, audio, assets) },
        { kind: 'action', label: 'Save Project As…', shortcut: 'Ctrl+Shift+S', action: () => saveProjectFileAs(store, audio, assets) },
        { kind: 'separator' },
        {
          kind: 'submenu',
          label: 'Import',
          items: [
            { kind: 'action', label: 'Audio File…', action: () => audioFileInput?.click() },
            { kind: 'action', label: 'Cover Image…', action: () => coverFileInput?.click() },
          ],
        },
        { kind: 'action', label: 'Export Capsule', action: () => renderCapsule(store, audio, assets) },
        { kind: 'separator' },
        { kind: 'action', label: 'Settings…', action: () => (uiState.settingsOpen = true) },
      ],
    },
    {
      label: 'Edit',
      items: [
        { kind: 'action', label: 'Undo', shortcut: 'Ctrl+Z', disabled: !store.history.canUndo, action: () => store.undo() },
        { kind: 'action', label: 'Redo', shortcut: 'Ctrl+Shift+Z', disabled: !store.history.canRedo, action: () => store.redo() },
        { kind: 'separator' },
        { kind: 'action', label: 'Duplicate Layer', shortcut: 'Ctrl+D', disabled: !store.selection.layerId, action: () => store.selection.layerId && store.duplicateLayer(store.selection.layerId) },
        {
          kind: 'action',
          label: 'Delete Selection',
          shortcut: 'Del',
          disabled: !store.selection.keyframeIds.size && !store.selection.layerId,
          action: () => deleteSelection(store),
        },
        {
          kind: 'action',
          label: 'Select All in Track',
          shortcut: 'Ctrl+A',
          disabled: !(store.selection.layerId && store.selection.trackKey),
          action: () => {
            const layer = store.project.layers.find((l) => l.id === store.selection.layerId);
            if (layer && store.selection.trackKey) store.selection.setKeyframeSelection(layer.tracks[store.selection.trackKey].map((k) => k.id));
          },
        },
      ],
    },
    {
      label: 'View',
      items: [
        {
          kind: 'submenu',
          label: 'Dockers',
          items: dockers.map((id) => ({
            kind: 'checkbox' as const,
            label: DOCK_PANEL_TITLES[id],
            checked: dockManager.isOpen(id),
            action: () => dockManager.toggle(id),
          })),
        },
        { kind: 'action', label: 'Reset Layout', action: () => dockManager.resetLayout() },
        { kind: 'separator' },
        { kind: 'action', label: 'Zoom Timeline In', shortcut: '+', action: () => store.zoomTimeline(1) },
        { kind: 'action', label: 'Zoom Timeline Out', shortcut: '-', action: () => store.zoomTimeline(-1) },
      ],
    },
    {
      label: 'Tools',
      items: [
        { kind: 'action', label: 'Render Capsule', action: () => renderCapsule(store, audio, assets) },
        { kind: 'action', label: 'Lyrics Importer…', action: () => (uiState.lyricsImporterOpen = true) },
        { kind: 'separator' },
        {
          kind: 'action',
          label: 'Save World (.spectral)',
          action: async () => {
            appMode.mode = 'world';
            await saveWorldFile();
          },
        },
        {
          kind: 'action',
          label: 'Save World As…',
          action: async () => {
            appMode.mode = 'world';
            await saveWorldFileAs();
          },
        },
        {
          kind: 'action',
          label: 'Load World…',
          action: async () => {
            appMode.mode = 'world';
            await tick();
            worldFileInput?.click();
          },
        },
        {
          kind: 'action',
          label: 'Generate World Files (Legacy Tracklist)',
          // A mode switch tears down/rebuilds the live dockview in
          // DockviewLayout.svelte's `$effect` — `tick()` lets that finish
          // before focusOrOpen looks for a panel in the new one.
          action: async () => {
            appMode.mode = 'world';
            worldStore.generate();
            await tick();
            dockManager.focusOrOpen('world');
          },
        },
        {
          kind: 'action',
          label: 'Generate Story Files',
          action: async () => {
            appMode.mode = 'capsule';
            storyStore.generate();
            await tick();
            dockManager.focusOrOpen('story');
          },
        },
        { kind: 'separator' },
        { kind: 'action', label: 'SVG Maker', action: async () => { appMode.mode = 'capsule'; await tick(); dockManager.focusOrOpen('svgmaker'); } },
        { kind: 'action', label: 'Script Console', action: async () => { appMode.mode = 'capsule'; await tick(); dockManager.focusOrOpen('script'); } },
        { kind: 'action', label: 'Assets', action: async () => { appMode.mode = 'capsule'; await tick(); dockManager.focusOrOpen('assets'); } },
      ],
    },
    {
      label: 'Help',
      items: [
        { kind: 'action', label: 'Keyboard Shortcuts', shortcut: '?', action: () => (uiState.helpOpen = true) },
        { kind: 'action', label: 'About Visualizer Studio', action: () => (uiState.aboutOpen = true) },
      ],
    },
  ]);

  let openIndex: number | null = $state(null);
  function toggle(i: number) {
    openIndex = openIndex === i ? null : i;
  }
  function onEnter(i: number) {
    if (openIndex !== null) openIndex = i;
  }
  function closeAll() {
    openIndex = null;
  }
</script>

<svelte:window onclick={closeAll} onkeydown={(e) => e.key === 'Escape' && closeAll()} />

<!-- svelte-ignore a11y_click_events_have_key_events -->
<!-- svelte-ignore a11y_no_static_element_interactions -->
<div class="menuBar" onclick={(e) => e.stopPropagation()}>
  {#each menus as menu, i (menu.label)}
    <div class="menuItem">
      <button
        class="topLabel"
        class:open={openIndex === i}
        aria-haspopup="menu"
        aria-expanded={openIndex === i}
        onclick={() => toggle(i)}
        onmouseenter={() => onEnter(i)}
      >
        {menu.label}
      </button>
      {#if openIndex === i}
        <div class="dropdown">
          <MenuList items={menu.items} onAction={closeAll} />
        </div>
      {/if}
    </div>
  {/each}
</div>

<input bind:this={projectFileInput} type="file" accept=".spex,application/json" hidden onchange={onProjectFileChosen} />
<input bind:this={audioFileInput} type="file" accept="audio/*" hidden onchange={onAudioFileChosen} />
<input bind:this={coverFileInput} type="file" accept="image/*" hidden onchange={onCoverFileChosen} />
<input bind:this={worldFileInput} type="file" accept=".spectral" hidden onchange={onWorldFileChosen} />

<style>
  .menuBar {
    display: flex;
    align-items: center;
    background: var(--bg1);
    border-bottom: 1px solid var(--line);
    padding: 0 4px;
    font: 11px var(--mono);
    user-select: none;
  }
  .menuItem {
    position: relative;
  }
  .topLabel {
    background: none;
    border: none;
    color: var(--dim);
    padding: 6px 10px;
    border-radius: 0;
  }
  .topLabel:hover,
  .topLabel.open {
    background: var(--bg3);
    color: var(--text);
  }
  .dropdown {
    position: absolute;
    left: 0;
    top: 100%;
    background: var(--bg2);
    border: 1px solid var(--line2);
    border-radius: 0 4px 4px 4px;
    box-shadow: 0 8px 24px rgba(0, 0, 0, 0.5);
    z-index: 2500;
  }
</style>
