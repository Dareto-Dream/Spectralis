<script lang="ts">
  import { onMount, onDestroy, mount, unmount, type Component } from 'svelte';
  import { DockviewComponent, type IDockviewPanel } from 'dockview-core';
  import 'dockview-core/dist/styles/dockview.css';
  import type { ProjectStore } from '../state/project.svelte';
  import type { AudioState } from '../state/audio.svelte';
  import LayersPanel from './LayersPanel.svelte';
  import InspectorPanel from './InspectorPanel.svelte';
  import PreviewCanvas from '../preview/PreviewCanvas.svelte';
  import TimelinePanelContent from './TimelinePanelContent.svelte';
  import Layers from '@lucide/svelte/icons/layers';
  import MonitorPlay from '@lucide/svelte/icons/monitor-play';
  import SlidersHorizontal from '@lucide/svelte/icons/sliders-horizontal';
  import ChartGantt from '@lucide/svelte/icons/chart-gantt';
  import LayoutTemplate from '@lucide/svelte/icons/layout-template';

  let { store, audio }: { store: ProjectStore; audio: AudioState } = $props();

  let container: HTMLDivElement | undefined = $state();
  let dv: DockviewComponent | undefined;
  let saveTimer: ReturnType<typeof setTimeout> | null = null;

  // Bumped to v2 when the default arrangement changed (preview/timeline column
  // + inspector/layers column, plus the window toolbar) — a v1 save from
  // before that redesign would otherwise silently mask it on next load.
  const LAYOUT_KEY = 'visualizer-studio:layout:v2';

  // Window/panel toolbar (plan follow-up: "toolbar + docker system for better
  // management"). Layout is two columns: Preview stacked above Timeline on the
  // left (the main working area), Inspector stacked above Layers on the right
  // (the properties rail) — AE's composition/timeline + effect-controls split.
  type PanelId = 'layers' | 'preview' | 'inspector' | 'timeline';
  const PANEL_TITLES: Record<PanelId, string> = {
    preview: 'Preview',
    timeline: 'Timeline',
    inspector: 'Inspector',
    layers: 'Layers',
  };
  const TOOLBAR_ORDER: { id: PanelId; icon: Component<{ size?: number }> }[] = [
    { id: 'preview', icon: MonitorPlay },
    { id: 'timeline', icon: ChartGantt },
    { id: 'inspector', icon: SlidersHorizontal },
    { id: 'layers', icon: Layers },
  ];

  let openPanelIds: Set<string> = $state(new Set());
  function syncOpenIds() {
    openPanelIds = new Set(dv?.api.panels.map((p) => p.id) ?? []);
  }

  // Reference-panel chain for each id, in priority order — re-adding a panel
  // after it's been closed picks the first still-open candidate to dock
  // against, so the two-column arrangement re-forms itself instead of the
  // panel landing wherever dockview's default fallback happens to put it.
  const POSITION_CHAIN: Record<PanelId, { direction: 'right' | 'below'; candidates: PanelId[] } | null> = {
    preview: null,
    timeline: { direction: 'below', candidates: ['preview', 'inspector', 'layers'] },
    inspector: { direction: 'right', candidates: ['preview', 'timeline', 'layers'] },
    layers: { direction: 'below', candidates: ['inspector', 'preview', 'timeline'] },
  };

  function positionFor(id: PanelId) {
    const chain = POSITION_CHAIN[id];
    if (!chain || !dv) return undefined;
    for (const candidate of chain.candidates) {
      if (dv.api.getPanel(candidate)) return { direction: chain.direction, referencePanel: candidate };
    }
    return undefined;
  }

  function addPanelById(id: PanelId) {
    if (!dv || dv.api.getPanel(id)) return;
    dv.addPanel({ id, component: id, title: PANEL_TITLES[id], position: positionFor(id) });
    if (id === 'timeline') dv.api.getPanel('timeline')?.api.setSize({ height: 260 });
    if (id === 'inspector' || id === 'layers') dv.api.getPanel(id)?.api.setSize({ width: 360 });
  }

  function togglePanel(id: PanelId) {
    const existing = dv?.api.getPanel(id);
    if (existing) dv?.removePanel(existing);
    else addPanelById(id);
  }

  function addDefaultPanels() {
    if (!dv) return;
    addPanelById('preview');
    addPanelById('timeline');
    addPanelById('inspector');
    addPanelById('layers');
  }

  function resetLayout() {
    dv?.clear();
    addDefaultPanels();
  }

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

    // Panel layout is UI chrome, not project data — persisted separately from
    // project autosave and explicitly not part of undo/redo. Falls back to the
    // default arrangement if nothing's saved yet or the saved JSON is stale/bad.
    let restored = false;
    try {
      const raw = localStorage.getItem(LAYOUT_KEY);
      if (raw) {
        dv.fromJSON(JSON.parse(raw));
        restored = true;
      }
    } catch {
      restored = false;
    }
    if (!restored) addDefaultPanels();
    syncOpenIds();
    dv.api.onDidAddPanel(syncOpenIds);
    dv.api.onDidRemovePanel(syncOpenIds);

    dv.onDidLayoutChange(() => {
      if (saveTimer) clearTimeout(saveTimer);
      saveTimer = setTimeout(() => {
        try {
          localStorage.setItem(LAYOUT_KEY, JSON.stringify(dv!.toJSON()));
        } catch {
          // best-effort — layout persistence is a convenience, never fatal
        }
      }, 400);
    });
  });

  onDestroy(() => dv?.dispose());
</script>

<div class="dockViewRoot">
  <div class="windowToolbar">
    {#each TOOLBAR_ORDER as { id, icon: Icon } (id)}
      <button
        class="small ghost"
        class:active={openPanelIds.has(id)}
        title={openPanelIds.has(id) ? `Hide ${PANEL_TITLES[id]}` : `Show ${PANEL_TITLES[id]}`}
        aria-label={openPanelIds.has(id) ? `Hide ${PANEL_TITLES[id]} panel` : `Show ${PANEL_TITLES[id]} panel`}
        aria-pressed={openPanelIds.has(id)}
        onclick={() => togglePanel(id)}
      >
        <Icon size={13} />
        {PANEL_TITLES[id]}
      </button>
    {/each}
    <span class="spacer"></span>
    <button class="small ghost" title="Restore the default panel arrangement" onclick={resetLayout}>
      <LayoutTemplate size={13} /> Reset Layout
    </button>
  </div>
  <div class="dockRoot" bind:this={container}></div>
</div>

<style>
  .dockViewRoot {
    display: flex;
    flex-direction: column;
    width: 100%;
    height: 100%;
    min-height: 0;
  }
  .windowToolbar {
    display: flex;
    align-items: center;
    gap: 4px;
    padding: 4px 8px;
    flex-shrink: 0;
    background: var(--bg1);
    border-bottom: 1px solid var(--line);
  }
  .windowToolbar .spacer {
    flex: 1;
  }
  .windowToolbar button.active {
    background: var(--bg3);
    border-color: var(--line2);
    color: var(--accent);
  }
  .dockRoot {
    flex: 1;
    min-height: 0;
    width: 100%;
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
