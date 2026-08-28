<script lang="ts">
  import { ProjectStore } from './state/project.svelte';
  import { AudioState } from './state/audio.svelte';
  import { AssetsState } from './state/assets.svelte';
  import MenuBar from './panels/MenuBar.svelte';
  import ActionBar from './panels/ActionBar.svelte';
  import TopBar from './panels/TopBar.svelte';
  import DockviewLayout from './panels/DockviewLayout.svelte';
  import Toast from './panels/Toast.svelte';
  import ConfirmModal from './panels/ConfirmModal.svelte';
  import ContextMenu from './panels/ContextMenu.svelte';
  import HelpModal from './panels/HelpModal.svelte';
  import SettingsModal from './panels/SettingsModal.svelte';
  import AboutModal from './panels/AboutModal.svelte';
  import ScriptEditorModal from './panels/ScriptEditorModal.svelte';
  import AutosaveBanner from './panels/AutosaveBanner.svelte';
  import { createGlobalKeymap } from './lib/keymap';
  import { autosave } from './state/autosave.svelte';
  import { themeSettings } from './state/theme.svelte';
  import { uiState } from './state/uiState.svelte';

  // Always boots blank now — the old "Neon Edit" starter project used to load
  // silently on cold start, which is exactly the kind of implicit preset the
  // Templates rework moved into Assets > Templates instead (pick one
  // deliberately from there, via store.loadProject(tpl.build())).
  const store = new ProjectStore();
  const audio = new AudioState();
  const assets = new AssetsState();

  themeSettings.apply();

  const onKeyDown = createGlobalKeymap(store, audio, assets, () => (uiState.helpOpen = true));

  // Debounce-write to localStorage on every committed edit — see AutosaveManager
  // for why this is a safety net, not a replacement for Save Project.
  $effect(() => {
    void store.history.undoStack.length; // reactive dependency: fires on every commit
    autosave.scheduleSave($state.snapshot(store.project));
  });

  function onBeforeUnload(e: BeforeUnloadEvent) {
    if (store.history.hasUncommittedSinceLoad && autosave.isStale()) {
      e.preventDefault();
      e.returnValue = '';
    }
  }
</script>

<svelte:window onkeydown={onKeyDown} onbeforeunload={onBeforeUnload} />

<div class="app">
  <AutosaveBanner {store} />
  <MenuBar {store} {audio} {assets} />
  <ActionBar {store} {audio} {assets} />
  <TopBar {store} {audio} {assets} />
  <div class="dockWrap">
    <DockviewLayout {store} {audio} {assets} />
  </div>
</div>
<Toast />
<ConfirmModal />
<ContextMenu />
<HelpModal open={uiState.helpOpen} onClose={() => (uiState.helpOpen = false)} />
<SettingsModal open={uiState.settingsOpen} onClose={() => (uiState.settingsOpen = false)} />
<AboutModal open={uiState.aboutOpen} onClose={() => (uiState.aboutOpen = false)} />
<ScriptEditorModal />

<style>
  .app {
    display: flex;
    flex-direction: column;
    width: 100%;
    height: 100%;
  }
  .dockWrap {
    flex: 1;
    min-height: 0;
  }
</style>
