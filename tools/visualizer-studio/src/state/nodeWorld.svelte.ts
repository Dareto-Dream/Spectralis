// World mode's node-graph state — module singleton, same pattern as
// WorldStore/StoryStore (closable dockview panel + reachable from outside
// the component tree). Deliberately separate from the legacy WorldStore
// (tracklist/level-map) rather than replacing it — that generator still
// works and exports fine; it's surfaced as a template now instead (see
// AssetsPanel's Templates category / the plan's "presets -> Assets"
// principle), not deleted.
//
// No undo/redo yet — same known gap the legacy WorldStore already has.
import { newSceneNode, type SceneNode, type SpritesheetConfig } from '../types/node';

export interface NodeWorldMeta {
  name: string;
  author: string;
}

// Re-exported so lib/worldSave.ts can narrow WorldFile.nodeGraph (typed
// `unknown` in format/worldFile.ts to avoid a state/ -> format/ coupling)
// back to the real shape without importing this whole module circularly.
export type { SceneNode } from '../types/node';

interface NodeRef {
  node: SceneNode;
  siblings: SceneNode[];
}

export class NodeWorldStore {
  meta: NodeWorldMeta = $state({ name: 'Untitled World', author: '' });
  roots: SceneNode[] = $state([]);
  selectedId: string | null = $state(null);
  // Bumped by every mutator below — NodeWorldStore has no undo/history stack
  // (yet) to key a "something changed" effect off of, unlike ProjectStore's
  // `history.undoStack.length`. This is that same signal for World's autosave.
  revision = $state(0);
  private bump() {
    this.revision++;
  }

  // Mirrors ProjectStore.knownFilePath/HistoryStack's dirty flag for World —
  // see those for the full reasoning (Ctrl+S reuse + the "unsaved changes"
  // prompt getting stuck on forever after the first edit, even right after
  // a save). savedAtRevision instead of a plain boolean since there's no
  // commit()-shaped choke point here to flip a flag from — comparing against
  // the same ever-increasing counter autosave already uses is simpler than
  // adding one.
  knownFilePath: string | null = $state(null);
  private savedAtRevision = $state(0);
  get isDirty(): boolean {
    return this.revision > this.savedAtRevision;
  }
  markSaved(path: string | null) {
    this.knownFilePath = path;
    this.savedAtRevision = this.revision;
  }

  get selectedNode(): SceneNode | null {
    return this.selectedId ? (this.findNode(this.selectedId)?.node ?? null) : null;
  }

  // Depth-first search returning the node plus the array it currently lives
  // in (roots, or some ancestor's children) — no parent-pointer field on
  // SceneNode itself, so this is how delete/duplicate/reorder locate where
  // to splice without walking the tree twice.
  findNode(id: string, list: SceneNode[] = this.roots): NodeRef | null {
    for (const n of list) {
      if (n.id === id) return { node: n, siblings: list };
      const found = this.findNode(id, n.children);
      if (found) return found;
    }
    return null;
  }

  countAll(list: SceneNode[] = this.roots): number {
    return list.reduce((n, node) => n + 1 + this.countAll(node.children), 0);
  }

  // Returns the REACTIVE object living inside `this.roots`/a parent's
  // `children`, not the plain pre-$state one just constructed — same trap
  // ProjectStore.addLayer documents: Svelte wraps a pushed object in a new
  // proxy identity, so callers need the live reference to see later
  // mutations (e.g. this test-covered path: toggleAsset/toggleScript/
  // updateTransform all look nodes up fresh via findNode, but a caller
  // holding onto the return value of addNode needs it to be the same object).
  addNode(parentId: string | null): SceneNode {
    const node = newSceneNode({ name: `Node ${this.countAll() + 1}` });
    if (parentId) {
      const ref = this.findNode(parentId);
      if (ref) {
        ref.node.children.push(node);
        const reactive = ref.node.children[ref.node.children.length - 1];
        this.selectedId = reactive.id;
        this.bump();
        return reactive;
      }
    }
    this.roots.push(node);
    const reactive = this.roots[this.roots.length - 1];
    this.selectedId = reactive.id;
    this.bump();
    return reactive;
  }

  deleteNode(id: string) {
    const ref = this.findNode(id);
    if (!ref) return;
    const idx = ref.siblings.indexOf(ref.node);
    if (idx >= 0) ref.siblings.splice(idx, 1);
    if (this.selectedId === id) this.selectedId = null;
    this.bump();
  }

  duplicateNode(id: string): SceneNode | null {
    const ref = this.findNode(id);
    if (!ref) return null;
    const clone = structuredClone($state.snapshot(ref.node)) as SceneNode;
    const remapIds = (n: SceneNode) => {
      n.id = `node_${crypto.randomUUID()}`;
      n.children.forEach(remapIds);
    };
    remapIds(clone);
    clone.name = `${clone.name} copy`;
    const idx = ref.siblings.indexOf(ref.node);
    ref.siblings.splice(idx + 1, 0, clone);
    const reactive = ref.siblings[idx + 1];
    this.selectedId = reactive.id;
    this.bump();
    return reactive;
  }

  updateTransform(id: string, patch: Partial<Pick<SceneNode, 'x' | 'y' | 'rotation' | 'scale'>>) {
    const ref = this.findNode(id);
    if (ref) {
      Object.assign(ref.node, patch);
      this.bump();
    }
  }

  rename(id: string, name: string) {
    const ref = this.findNode(id);
    if (ref && name.trim()) {
      ref.node.name = name.trim();
      this.bump();
    }
  }

  toggleAsset(id: string, assetId: string) {
    const ref = this.findNode(id);
    if (!ref) return;
    const i = ref.node.assetIds.indexOf(assetId);
    if (i >= 0) ref.node.assetIds.splice(i, 1);
    else ref.node.assetIds.push(assetId);
    this.bump();
  }

  toggleScript(id: string, scriptId: string) {
    const ref = this.findNode(id);
    if (!ref) return;
    const i = ref.node.scriptIds.indexOf(scriptId);
    if (i >= 0) ref.node.scriptIds.splice(i, 1);
    else ref.node.scriptIds.push(scriptId);
    this.bump();
  }

  setSpritesheet(id: string, config: SpritesheetConfig | null) {
    const ref = this.findNode(id);
    if (ref) {
      ref.node.spritesheet = config;
      this.bump();
    }
  }

  updateMeta(patch: Partial<NodeWorldMeta>) {
    Object.assign(this.meta, patch);
    this.bump();
  }

  loadGraph(roots: SceneNode[], meta?: Partial<NodeWorldMeta>, knownFilePath: string | null = null) {
    this.roots = roots;
    if (meta) Object.assign(this.meta, meta);
    this.selectedId = null;
    this.knownFilePath = knownFilePath;
    // Also syncs savedAtRevision to the CURRENT revision (not bumping it) —
    // a freshly loaded graph exactly matches what's on disk, same "just
    // loaded = not dirty" rule ProjectStore.loadProject follows. Deliberately
    // no bump() beyond that — loading a graph (including the autosave
    // restore flow itself) must not immediately re-trigger a fresh autosave
    // write of the thing that was just read.
    this.savedAtRevision = this.revision;
  }

  clear() {
    this.roots = [];
    this.selectedId = null;
    this.bump();
  }
}

export const nodeWorldStore = new NodeWorldStore();
