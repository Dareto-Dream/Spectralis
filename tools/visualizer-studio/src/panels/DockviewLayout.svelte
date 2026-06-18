<script lang="ts">
  import { onMount, onDestroy, mount, unmount, type Component } from 'svelte';
  import { DockviewComponent } from 'dockview-core';
  import 'dockview-core/dist/styles/dockview.css';
  import type { ProjectStore } from '../state/project.svelte';
  import type { AudioState } from '../state/audio.svelte';
  import LayersPanel from './LayersPanel.svelte';
  import InspectorPanel from './InspectorPanel.svelte';
  import PreviewCanvas from '../preview/PreviewCanvas.svelte';
  import TimelinePanelContent from './TimelinePanelContent.svelte';

  let { store, audio }: { store: ProjectStore; audio: AudioState } = $props();

  let container: HTMLDivElement | undefined = $state();
  let dv: DockviewComponent | undefined;

  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  const PANEL_COMPONENTS: Record<string, Component<any>> = {
    layers: LayersPanel,
    preview: PreviewCanvas,
    inspector: InspectorPanel,
    timeline: TimelinePanelContent,
  };

  onMount(() => {
    if (!container) return;
    dv = new DockviewComponent(container, {
      className: 'dockview-theme-abyss',
      createComponent: (options) => {
        const el = document.createElement('div');
        el.style.height = '100%';
        el.style.overflow = 'auto';
        // eslint-disable-next-line @typescript-eslint/no-explicit-any
        let instance: any;
        return {
          element: el,
          init: () => {
            const Comp = PANEL_COMPONENTS[options.name];
            if (Comp) instance = mount(Comp, { target: el, props: { store, audio } });
          },
          dispose: () => {
            if (instance) unmount(instance);
          },
        };
      },
    });

    dv.addPanel({ id: 'layers', component: 'layers', title: 'Layers' });
    dv.addPanel({
      id: 'preview',
      component: 'preview',
      title: 'Preview',
      position: { direction: 'right', referencePanel: 'layers' },
    });
    dv.addPanel({
      id: 'inspector',
      component: 'inspector',
      title: 'Inspector',
      position: { direction: 'right', referencePanel: 'preview' },
    });
    dv.addPanel({
      id: 'timeline',
      component: 'timeline',
      title: 'Timeline',
      position: { direction: 'below', referencePanel: 'layers' },
    });
    // Give the timeline the lion's share of vertical space, matching the old tool.
    dv.api.panels.find((p) => p.id === 'timeline')?.api.setSize({ height: 240 });
  });

  onDestroy(() => dv?.dispose());
</script>

<div class="dockRoot" bind:this={container}></div>

<style>
  .dockRoot {
    width: 100%;
    height: 100%;
  }
  :global(.dockview-theme-abyss) {
    --dv-group-view-background-color: var(--bg0);
    --dv-tabs-and-actions-container-background-color: var(--bg1);
    --dv-activegroup-visiblepanel-tab-background-color: var(--bg2);
    --dv-activegroup-hiddenpanel-tab-background-color: var(--bg1);
    --dv-inactivegroup-visiblepanel-tab-background-color: var(--bg1);
    --dv-inactivegroup-hiddenpanel-tab-background-color: var(--bg1);
    --dv-tab-divider-color: var(--line);
    --dv-separator-border: var(--line);
    --dv-paneview-header-border-color: var(--line);
    --dv-activegroup-visiblepanel-tab-color: var(--text);
    --dv-activegroup-hiddenpanel-tab-color: var(--dim);
    --dv-inactivegroup-visiblepanel-tab-color: var(--dim);
    --dv-inactivegroup-hiddenpanel-tab-color: var(--dim2);
    --dv-paneview-active-outline-color: var(--accent);
  }
</style>
