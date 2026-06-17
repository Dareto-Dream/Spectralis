<script lang="ts">
  // Phase 5 verification harness — real dockview/panel layout arrives in Phase 6,
  // this just proves the preview canvas + timeline both work against the fixture.
  import { ProjectStore } from './state/project.svelte';
  import { AudioState } from './state/audio.svelte';
  import PreviewCanvas from './preview/PreviewCanvas.svelte';
  import TimelineCanvas from './timeline/TimelineCanvas.svelte';
  import fixture from '../tests/fixtures/fixture-project.json';
  import type { AnimKey, Project } from './types/project';
  import { ANIM_KEYS } from './types/project';

  const store = new ProjectStore();
  store.loadProject(fixture as Project);
  const audio = new AudioState();

  function onAudioPicked(e: Event) {
    const file = (e.target as HTMLInputElement).files?.[0];
    if (file) audio.loadFile(file);
  }

  function selectTrack(key: AnimKey) {
    store.selection.selectTrack(key);
  }
</script>

<main>
  <h1>Visualizer Studio — Phase 5 check</h1>
  <div class="controls">
    <button onclick={() => store.togglePlay()}>{store.playing ? 'Pause' : 'Play'}</button>
    <button onclick={() => store.stop()}>Stop</button>
    <button onclick={() => store.undo()} disabled={!store.history.canUndo}>Undo</button>
    <button onclick={() => store.redo()} disabled={!store.history.canRedo}>Redo</button>
    <input type="file" accept="audio/*" onchange={onAudioPicked} />
    {#if audio.error}<span class="err">{audio.error}</span>{/if}
  </div>
  <div class="layout">
    <div class="layers">
      {#each store.project.layers as layer (layer.id)}
        <button
          class="layerBtn"
          class:active={store.selection.layerId === layer.id}
          onclick={() => store.selection.selectLayer(layer.id)}
        >
          {layer.name}
        </button>
        {#if store.selection.layerId === layer.id}
          <div class="trackRow">
            {#each ANIM_KEYS as key (key)}
              {#if layer.tracks[key].length}
                <button
                  class="trackBtn"
                  class:active={store.selection.trackKey === key}
                  onclick={() => selectTrack(key)}
                >
                  {key} ({layer.tracks[key].length})
                </button>
              {/if}
            {/each}
          </div>
        {/if}
      {/each}
    </div>
    <PreviewCanvas {store} {audio} />
  </div>
  <TimelineCanvas {store} {audio} />
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
  .layout {
    display: flex;
    gap: 16px;
  }
  .layers {
    display: flex;
    flex-direction: column;
    gap: 4px;
    min-width: 160px;
  }
  .layerBtn,
  .trackBtn {
    text-align: left;
  }
  .layerBtn.active,
  .trackBtn.active {
    background: var(--accent2);
    color: #000;
  }
  .trackRow {
    display: flex;
    flex-wrap: wrap;
    gap: 4px;
    margin: 2px 0 6px 12px;
  }
  .err {
    color: var(--danger);
  }
</style>
