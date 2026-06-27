// Bridges the menu bar's View > Dockers checkboxes (and Tools > ... "open
// panel" actions) to the single live DockviewComponent instance owned by
// DockviewLayout.svelte. There's only ever one dockview in this app, so a
// module singleton is simpler than prop-drilling a component reference
// through App.svelte/MenuBar.svelte — same pattern as toast/confirmModal.
export type DockPanelId = 'preview' | 'timeline' | 'inspector' | 'layers' | 'assets' | 'world' | 'story' | 'script' | 'svgmaker';

export const DOCK_PANEL_TITLES: Record<DockPanelId, string> = {
  preview: 'Preview',
  timeline: 'Timeline',
  inspector: 'Inspector',
  layers: 'Layers',
  assets: 'Assets',
  world: 'World Editor',
  story: 'Story Editor',
  script: 'Script Console',
  svgmaker: 'SVG Maker',
};

interface DockHost {
  isOpen(id: DockPanelId): boolean;
  open(id: DockPanelId): void;
  close(id: DockPanelId): void;
  focusOrOpen(id: DockPanelId): void;
  resetLayout(): void;
}

class DockManager {
  openPanelIds: Set<string> = $state(new Set());
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
}

export const dockManager = new DockManager();
