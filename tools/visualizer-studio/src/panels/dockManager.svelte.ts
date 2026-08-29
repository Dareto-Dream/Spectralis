// Bridges the menu bar's View > Dockers checkboxes (and Tools > ... "open
// panel" actions) to the single live DockviewComponent instance owned by
// DockviewLayout.svelte. There's only ever one dockview in this app, so a
// module singleton is simpler than prop-drilling a component reference
// through App.svelte/MenuBar.svelte — same pattern as toast/confirmModal.
export type DockPanelId =
  | 'preview'
  | 'workspace'
  | 'tools'
  | 'timeline'
  | 'inspector'
  | 'layers'
  | 'assets'
  | 'world'
  | 'story'
  | 'script'
  | 'svgmaker'
  | 'nodeGraph'
  | 'nodeInspector'
  | 'spriteEditor';

export const DOCK_PANEL_TITLES: Record<DockPanelId, string> = {
  preview: 'Preview',
  workspace: 'Workspace',
  tools: 'Tools',
  timeline: 'Timeline',
  inspector: 'Inspector',
  layers: 'Layers',
  assets: 'Assets',
  world: 'World Workspace (Legacy Tracklist)',
  story: 'Story Editor',
  script: 'Script Console',
  svgmaker: 'SVG Maker',
  nodeGraph: 'World Workspace',
  nodeInspector: 'Node Inspector',
  spriteEditor: 'Sprite Editor',
};

interface DockHost {
  isOpen(id: DockPanelId): boolean;
  open(id: DockPanelId): void;
  close(id: DockPanelId): void;
  focusOrOpen(id: DockPanelId): void;
  resetLayout(): void;
  // Back the Capsule Workspace/Preview/Story viewport switch in ActionBar —
  // "Preview" temporarily fills the whole dockview (dockview-core's own
  // maximize-group), giving it the "watch your capsule, no editing chrome
  // in the way" feel the plan calls for; Workspace/Story exit that.
  maximize(id: DockPanelId): void;
  exitMaximized(): void;
}

class DockManager {
  openPanelIds: Set<string> = $state(new Set());
  // Which panel is frontmost in its tab group — World/Story/SVG Maker share a
  // group with Preview (see POSITION_CHAIN in DockviewLayout.svelte), so this
  // is how ActionBar knows which of Studio/World/Story to highlight.
  activePanelId: string | null = $state(null);
  private host: DockHost | null = null;

  register(host: DockHost, initialOpenIds: Iterable<string>) {
    this.host = host;
    this.openPanelIds = new Set(initialOpenIds);
  }

  unregister() {
    this.host = null;
  }

  sync(openIds: Iterable<string>) {
    this.openPanelIds = new Set(openIds);
  }

  setActivePanel(id: string | null) {
    this.activePanelId = id;
  }

  isOpen(id: DockPanelId): boolean {
    return this.openPanelIds.has(id);
  }

  toggle(id: DockPanelId) {
    if (!this.host) return;
    if (this.host.isOpen(id)) this.host.close(id);
    else this.host.open(id);
  }

  // Used by menu items that mean "show me this panel" rather than "toggle
  // it" — Tools > Script Console, Tools > SVG Maker, double-clicking an
  // asset that opens the SVG maker, etc. Brings it to front if already open.
  focusOrOpen(id: DockPanelId) {
    this.host?.focusOrOpen(id);
  }

  resetLayout() {
    this.host?.resetLayout();
  }

  maximize(id: DockPanelId) {
    this.host?.maximize(id);
  }

  exitMaximized() {
    this.host?.exitMaximized();
  }
}

export const dockManager = new DockManager();
