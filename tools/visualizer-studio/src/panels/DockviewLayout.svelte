<script lang="ts">
  import { onDestroy, mount, unmount, type Component } from 'svelte';
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
  import WorkspaceCanvas from '../preview/WorkspaceCanvas.svelte';
  import ToolsPanel from './ToolsPanel.svelte';
  import WorldPanel from '../world-tab/WorldPanel.svelte';
  import NodeCanvas from '../world-tab/NodeCanvas.svelte';
  import NodeInspector from '../world-tab/NodeInspector.svelte';
  import SpriteEditor from '../world-tab/SpriteEditor.svelte';
  import StoryPanel from '../story-tab/StoryPanel.svelte';
  import ScriptConsole from './ScriptConsole.svelte';
  import SvgMaker from './SvgMaker.svelte';
  import { dockManager, DOCK_PANEL_TITLES, type DockPanelId } from './dockManager.svelte';
  import { appMode } from '../state/appMode.svelte';

  let { store, audio, assets }: { store: ProjectStore; audio: AudioState; assets: AssetsState } = $props();

  let container: HTMLDivElement | undefined = $state();
  let dv: DockviewComponent | undefined;
  let saveTimer: ReturnType<typeof setTimeout> | null = null;

  // Capsule and World are separate top-level modes (state/appMode.svelte.ts)
  // with genuinely different viewport sets — Capsule keeps the full
  // Layers/Timeline/Inspector/Assets/Story arrangement this app has always
  // had, World is just its (legacy, pre-node-graph) tracklist workspace +
  // a preview. Rather than one dockview/POSITION_CHAIN pretending to serve
  // both shapes, each mode gets its own config and the live DockviewComponent
  // is torn down and rebuilt when the mode switches.
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  interface ModeConfig {
    layoutKey: string;
    defaultOpen: DockPanelId[];
    // dockview-core's real Direction also includes 'left'/'above' — this app
    // only ever used 'right'/'below'/'within' until the Tools docker needed
    // a left-side default position, so the local type just tracked what was
    // in use rather than the library's full set.
    positionChain: Partial<Record<DockPanelId, { direction: 'left' | 'right' | 'below' | 'within'; candidates: DockPanelId[] }>>;
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    components: Partial<Record<DockPanelId, Component<any>>>;
  }

  // v2: the Workspace canvas + left Tools docker replace `preview` as the
  // default centerpiece — `preview` (still PreviewCanvas, still pointer-
  // handler-free) is now opened on demand by ActionBar's Preview viewport
  // button instead of being part of the normal layout, so drawing/point
  // editing never happens on the read-only viewport. A stale v1 save is
  // simply never read again under this new key, same as the v1 bump note.
  const CAPSULE_CONFIG: ModeConfig = {
    layoutKey: 'visualizer-studio:layout:capsule:v2',
    defaultOpen: ['tools', 'workspace', 'layers', 'timeline', 'inspector', 'assets'],
    positionChain: {
      workspace: { direction: 'right', candidates: ['tools'] },
      timeline: { direction: 'below', candidates: ['workspace', 'inspector', 'layers', 'assets'] },
      inspector: { direction: 'right', candidates: ['workspace', 'timeline', 'layers'] },
      layers: { direction: 'below', candidates: ['inspector', 'workspace', 'timeline'] },
      assets: { direction: 'within', candidates: ['timeline', 'workspace', 'inspector', 'layers'] },
      preview: { direction: 'within', candidates: ['workspace'] },
      story: { direction: 'within', candidates: ['workspace', 'timeline'] },
      svgmaker: { direction: 'within', candidates: ['workspace', 'story'] },
      script: { direction: 'within', candidates: ['assets', 'timeline', 'workspace'] },
    },
    components: {
      tools: ToolsPanel,
      workspace: WorkspaceCanvas,
      layers: LayersPanel,
      preview: PreviewCanvas,
      inspector: InspectorPanel,
      timeline: TimelinePanelContent,
      assets: AssetsPanel,
      story: StoryPanel,
      script: ScriptConsole,
      svgmaker: SvgMaker,
    },
  };

  // "2 viewports" (Workspace, Preview) per the plan — nodeGraph is the real
  // Workspace now (a node canvas, not layer-based); nodeInspector/spriteEditor
  // tab alongside it as the property rail; the legacy flat tracklist/level-
  // map generator (`world`) is still reachable via View > Dockers, not
  // deleted, just no longer the default — see AssetsPanel's Templates
  // category for where its old role (a ready-made starting point) landed.
  const WORLD_CONFIG: ModeConfig = {
    layoutKey: 'visualizer-studio:layout:world:v2',
    defaultOpen: ['nodeGraph', 'nodeInspector', 'preview'],
    positionChain: {
      nodeInspector: { direction: 'right', candidates: ['nodeGraph'] },
      preview: { direction: 'below', candidates: ['nodeInspector', 'nodeGraph'] },
      spriteEditor: { direction: 'within', candidates: ['nodeInspector'] },
      world: { direction: 'within', candidates: ['nodeGraph'] },
    },
    components: {
      world: WorldPanel,
      nodeGraph: NodeCanvas,
      nodeInspector: NodeInspector,
      spriteEditor: SpriteEditor,
      preview: PreviewCanvas,
    },
  };

  function configFor(mode: 'capsule' | 'world'): ModeConfig {
    return mode === 'world' ? WORLD_CONFIG : CAPSULE_CONFIG;
  }

  function positionFor(config: ModeConfig, id: DockPanelId) {
    const chain = config.positionChain[id];
    if (!chain || !dv) return undefined;
    for (const candidate of chain.candidates) {
      if (dv.api.getPanel(candidate)) return { direction: chain.direction, referencePanel: candidate };
    }
    return undefined;
  }

  function addPanelById(config: ModeConfig, id: DockPanelId) {
    if (!dv || !config.components[id] || dv.api.getPanel(id)) return;
    dv.addPanel({ id, component: id, title: DOCK_PANEL_TITLES[id], position: positionFor(config, id) });
    if (id === 'timeline') dv.api.getPanel('timeline')?.api.setSize({ height: 260 });
    if (id === 'inspector' || id === 'layers') dv.api.getPanel(id)?.api.setSize({ width: 280 });
    if (id === 'tools') dv.api.getPanel('tools')?.api.setSize({ width: 160 });
  }

  function addDefaultPanels(config: ModeConfig) {
    if (!dv) return;
    for (const id of config.defaultOpen) addPanelById(config, id);
  }

  function syncOpenIds() {
    dockManager.sync(dv?.api.panels.map((p) => p.id) ?? []);
  }

  function teardownDockview() {
    if (saveTimer) {
      clearTimeout(saveTimer);
      saveTimer = null;
    }
    dockManager.unregister();
    dv?.dispose();
    dv = undefined;
  }

  function buildDockview(config: ModeConfig) {
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
            const Comp = config.components[options.name as DockPanelId];
            if (Comp) instance = mount(Comp, { target: el, props: { store, audio, assets } });
          },
          dispose: () => {
            if (instance) unmount(instance);
          },
        };
      },
    });

    let restored = false;
    try {
      const raw = localStorage.getItem(config.layoutKey);
      if (raw) {
        dv.fromJSON(JSON.parse(raw));
        restored = true;
      }
    } catch {
      restored = false;
    }
    if (!restored) addDefaultPanels(config);
    syncOpenIds();
    dv.api.onDidAddPanel(syncOpenIds);
    dv.api.onDidRemovePanel(syncOpenIds);

    dockManager.setActivePanel(dv.api.activePanel?.id ?? null);
    dv.api.onDidActivePanelChange((panel) => dockManager.setActivePanel(panel?.id ?? null));

    dv.onDidLayoutChange(() => {
      if (saveTimer) clearTimeout(saveTimer);
      saveTimer = setTimeout(() => {
        try {
          localStorage.setItem(config.layoutKey, JSON.stringify(dv!.toJSON()));
        } catch {
          // best-effort — layout persistence is a convenience, never fatal
        }
      }, 400);
    });

    dockManager.register(
      {
        isOpen: (id) => !!dv?.api.getPanel(id),
        open: (id) => addPanelById(config, id),
        close: (id) => {
          const p = dv?.api.getPanel(id);
          if (p) dv?.removePanel(p);
        },
        focusOrOpen: (id) => {
          const p = dv?.api.getPanel(id);
          if (p) dv?.setActivePanel(p);
          else addPanelById(config, id);
        },
        resetLayout: () => {
          dv?.clear();
          addDefaultPanels(config);
        },
        maximize: (id) => {
          const p = dv?.api.getPanel(id);
          p?.api.maximize();
        },
        exitMaximized: () => dv?.api.exitMaximizedGroup(),
      },
      dv.api.panels.map((p) => p.id)
    );
  }

  // Rebuilds the whole dockview whenever the mode switches (including the
  // first run, once `container` exists) — Capsule ↔ World aren't tabs
  // within one layout, they're two independent ones.
  $effect(() => {
    const mode = appMode.mode;
    if (!container) return;
    teardownDockview();
    buildDockview(configFor(mode));
  });

  onDestroy(() => {
    teardownDockview();
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
