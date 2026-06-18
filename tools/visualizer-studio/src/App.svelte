<script lang="ts">
  import { ProjectStore } from './state/project.svelte';
  import { AudioState } from './state/audio.svelte';
  import TopBar from './panels/TopBar.svelte';
  import DockviewLayout from './panels/DockviewLayout.svelte';
  import Toast from './panels/Toast.svelte';
  import ConfirmModal from './panels/ConfirmModal.svelte';
  import ContextMenu from './panels/ContextMenu.svelte';
  import { TEMPLATES } from './lib/templates';

  const store = new ProjectStore();
  store.loadProject(TEMPLATES[0].build());
  const audio = new AudioState();
</script>

<div class="app">
  <TopBar {store} {audio} />
  <div class="dockWrap">
    <DockviewLayout {store} {audio} />
  </div>
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
</style>
