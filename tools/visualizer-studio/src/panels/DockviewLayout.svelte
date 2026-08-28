<script lang="ts">
  import { onMount, onDestroy, mount, unmount, type Component } from 'svelte';
  import { DockviewComponent } from 'dockview-core';
  import 'dockview-core/dist/styles/dockview.css';
  import type { ProjectStore } from '../state/project.svelte';
  import type { AudioState } from '../state/audio.svelte';
  import type { AssetsState } from '../state/assets.svelte';
  import LayersPanel from './LayersPanel.svelte';
  import InspectorPanel from './InspectorPanel.svelte';
  import PreviewCanvas from '../preview/PreviewCanvas.svelte';
  import TimelinePanelContent from './TimelinePanelContent.svelte';
  import AssetsPanel from './AssetsPanel.svelte';
  import WorldPanel from '../world-tab/WorldPanel.svelte';
  import StoryPanel from '../story-tab/StoryPanel.svelte';
  import ScriptConsole from './ScriptConsole.svelte';
  import SvgMaker from './SvgMaker.svelte';
  import { dockManager, DOCK_PANEL_TITLES, type DockPanelId } from './dockManager.svelte';

  let { store, audio, assets }: { store: ProjectStore; audio: AudioState; assets: AssetsState } = $props();

  let container: HTMLDivElement | undefined = $state();
  let dv: DockviewComponent | undefined;
  let saveTimer: ReturnType<typeof setTimeout> | null = null;

  // Bumped to v4: Assets now tabs with Timeline instead of stacking full-width
  // below it (was eating too much vertical space for two panels people mostly
  // use one-at-a-time), and the properties rail is narrower by default — a v3
  // save would otherwise pin the old heavier arrangement back on next load.
  const LAYOUT_KEY = 'visualizer-studio:layout:v4';

  const DEFAULT_OPEN: DockPanelId[] = ['preview', 'timeline', 'inspector', 'layers', 'assets'];

  // Reference-panel chain per id, in priority order — (re)opening a panel
  // picks the first still-open candidate to dock against, so the intended
  // arrangement re-forms itself regardless of what's currently open. Preview/
  // Timeline form the main column; Inspector/Layers the properties rail;
  // Assets tabs alongside Timeline instead of stacking below it — the two are
  // mostly used one-at-a-time, so a tab costs no vertical space; World/Story/
  // SVG Maker join Preview as alternate "viewport" tabs; Script Console joins
  // the Timeline/Assets group as a fellow utility panel — each one is its own
  // dockable viewport that can be opened independently rather than a
  // hard-coded tab switcher.
  const POSITION_CHAIN: Record<DockPanelId, { direction: 'right' | 'below' | 'within'; candidates: DockPanelId[] } | null> = {
    preview: null,
    timeline: { direction: 'below', candidates: ['preview', 'inspector', 'layers', 'assets'] },
    inspector: { direction: 'right', candidates: ['preview', 'timeline', 'layers'] },
    layers: { direction: 'below', candidates: ['inspector', 'preview', 'timeline'] },
    assets: { direction: 'within', candidates: ['timeline', 'preview', 'inspector', 'layers'] },
    world: { direction: 'within', candidates: ['preview', 'timeline', 'inspector'] },
    story: { direction: 'within', candidates: ['preview', 'world', 'timeline'] },
    svgmaker: { direction: 'within', candidates: ['preview', 'world', 'story'] },
    script: { direction: 'within', candidates: ['assets', 'timeline', 'preview'] },
  };

  function positionFor(id: DockPanelId) {
    const chain = POSITION_CHAIN[id];
    if (!chain || !dv) return undefined;
    for (const candidate of chain.candidates) {
      if (dv.api.getPanel(candidate)) return { direction: chain.direction, referencePanel: candidate };
    }
    return undefined;
  }

  function addPanelById(id: DockPanelId) {
    if (!dv || dv.api.getPanel(id)) return;
    dv.addPanel({ id, component: id, title: DOCK_PANEL_TITLES[id], position: positionFor(id) });
    if (id === 'timeline') dv.api.getPanel('timeline')?.api.setSize({ height: 260 });
    if (id === 'inspector' || id === 'layers') dv.api.getPanel(id)?.api.setSize({ width: 280 });
  }

  function addDefaultPanels() {
    if (!dv) return;
    for (const id of DEFAULT_OPEN) addPanelById(id);
  }

  function syncOpenIds() {
    dockManager.sync(dv?.api.panels.map((p) => p.id) ?? []);
  }

  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  const PANEL_COMPONENTS: Record<string, Component<any>> = {
    layers: LayersPanel,
    preview: PreviewCanvas,
    inspector: InspectorPanel,
    timeline: TimelinePanelContent,
    assets: AssetsPanel,
    world: WorldPanel,
    story: StoryPanel,
    script: ScriptConsole,
    svgmaker: SvgMaker,
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
            if (Comp) instance = mount(Comp, { target: el, props: { store, audio, assets } });
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

    dockManager.setActivePanel(dv.api.activePanel?.id ?? null);
    dv.api.onDidActivePanelChange((panel) => dockManager.setActivePanel(panel?.id ?? null));

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

    dockManager.register(
      {
        isOpen: (id) => !!dv?.api.getPanel(id),
        open: (id) => addPanelById(id),
        close: (id) => {
          const p = dv?.api.getPanel(id);
          if (p) dv?.removePanel(p);
        },
        focusOrOpen: (id) => {
          const p = dv?.api.getPanel(id);
          if (p) dv?.setActivePanel(p);
          else addPanelById(id);
        },
        resetLayout: () => {
          dv?.clear();
          addDefaultPanels();
        },
      },
      dv.api.panels.map((p) => p.id)
    );
  });

  onDestroy(() => {
    dockManager.unregister();
    dv?.dispose();
  });
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
