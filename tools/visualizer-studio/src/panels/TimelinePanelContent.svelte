<script lang="ts">
  import type { ProjectStore } from '../state/project.svelte';
  import type { AudioState } from '../state/audio.svelte';
  import TimelineCanvas from '../timeline/TimelineCanvas.svelte';
  import SectionEditor from './SectionEditor.svelte';

  let { store, audio }: { store: ProjectStore; audio: AudioState } = $props();
</script>

<div class="wrap">
  <TimelineCanvas {store} {audio} />
  <SectionEditor {store} />
</div>

<style>
  .wrap {
    display: flex;
    flex-direction: column;
    height: 100%;
  }
</style>
