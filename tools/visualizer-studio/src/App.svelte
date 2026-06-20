<script lang="ts">
  import { ProjectStore } from './state/project.svelte';
  import { AudioState } from './state/audio.svelte';
  import TopBar from './panels/TopBar.svelte';
  import DockviewLayout from './panels/DockviewLayout.svelte';
  import Toast from './panels/Toast.svelte';
  import ConfirmModal from './panels/ConfirmModal.svelte';
  import ContextMenu from './panels/ContextMenu.svelte';
  import WorldPanel from './world-tab/WorldPanel.svelte';
  import StoryPanel from './story-tab/StoryPanel.svelte';
  import { TEMPLATES } from './lib/templates';

  const store = new ProjectStore();
  store.loadProject(TEMPLATES[0].build());
  const audio = new AudioState();

  type Tab = 'studio' | 'world' | 'story';
  let tab: Tab = $state('studio');
</script>

<div class="app">
  <div class="tabs">
    <button class:active={tab === 'studio'} onclick={() => (tab = 'studio')}>Studio</button>
    <button class:active={tab === 'world'} onclick={() => (tab = 'world')}>World</button>
    <button class:active={tab === 'story'} onclick={() => (tab = 'story')}>Story</button>
  </div>
  {#if tab === 'studio'}
    <TopBar {store} {audio} />
  {/if}
  <div class="dockWrap" class:hidden={tab !== 'studio'}>
    <DockviewLayout {store} {audio} />
  </div>
  {#if tab === 'world'}
    <div class="tabContent"><WorldPanel /></div>
  {:else if tab === 'story'}
    <div class="tabContent"><StoryPanel /></div>
  {/if}
</div>
<Toast />
<ConfirmModal />
<ContextMenu />

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
  .dockWrap.hidden {
    display: none;
  }
  .tabs {
    display: flex;
    gap: 2px;
    padding: 4px 8px 0;
    background: var(--bg0);
    border-bottom: 1px solid var(--line);
  }
  .tabs button {
    background: none;
    border: none;
    border-radius: 4px 4px 0 0;
    padding: 6px 14px;
    font: 11px var(--mono);
    color: var(--dim);
  }
  .tabs button.active {
    background: var(--bg1);
    color: var(--text);
  }
  .tabContent {
    flex: 1;
    min-height: 0;
  }
</style>
