<script lang="ts">
  // Phase 4 verification harness — real dockview/panel layout arrives in Phase 6,
  // this just proves the preview canvas renders the fixture project correctly.
  import { ProjectStore } from './state/project.svelte';
  import { AudioState } from './state/audio.svelte';
  import PreviewCanvas from './preview/PreviewCanvas.svelte';
  import fixture from '../tests/fixtures/fixture-project.json';
  import type { Project } from './types/project';

  const store = new ProjectStore();
  store.loadProject(fixture as Project);
  const audio = new AudioState();

  function onAudioPicked(e: Event) {
    const file = (e.target as HTMLInputElement).files?.[0];
    if (file) audio.loadFile(file);
  }
</script>

<main>
  <h1>Visualizer Studio — Phase 4 check</h1>
  <div class="controls">
    <button onclick={() => store.togglePlay()}>{store.playing ? 'Pause' : 'Play'}</button>
    <button onclick={() => store.stop()}>Stop</button>
    <input type="file" accept="audio/*" onchange={onAudioPicked} />
    {#if audio.error}<span class="err">{audio.error}</span>{/if}
  </div>
  <PreviewCanvas {store} {audio} />
</main>

<style>
  main {
    padding: 24px;
  }
  .controls {
    display: flex;
    gap: 8px;
    align-items: center;
    margin-bottom: 12px;
  }
  .err {
    color: var(--danger);
  }
</style>
