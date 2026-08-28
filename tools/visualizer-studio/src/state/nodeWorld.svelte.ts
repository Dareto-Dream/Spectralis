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
        return reactive;
      }
    }
    this.roots.push(node);
    const reactive = this.roots[this.roots.length - 1];
    this.selectedId = reactive.id;
    return reactive;
  }

  deleteNode(id: string) {
    const ref = this.findNode(id);
    if (!ref) return;
    const idx = ref.siblings.indexOf(ref.node);
    if (idx >= 0) ref.siblings.splice(idx, 1);
    if (this.selectedId === id) this.selectedId = null;
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
    return reactive;
  }

  updateTransform(id: string, patch: Partial<Pick<SceneNode, 'x' | 'y' | 'rotation' | 'scale'>>) {
    const ref = this.findNode(id);
    if (ref) Object.assign(ref.node, patch);
  }

  rename(id: string, name: string) {
    const ref = this.findNode(id);
    if (ref && name.trim()) ref.node.name = name.trim();
  }

  toggleAsset(id: string, assetId: string) {
    const ref = this.findNode(id);
    if (!ref) return;
    const i = ref.node.assetIds.indexOf(assetId);
    if (i >= 0) ref.node.assetIds.splice(i, 1);
    else ref.node.assetIds.push(assetId);
  }

  toggleScript(id: string, scriptId: string) {
    const ref = this.findNode(id);
    if (!ref) return;
    const i = ref.node.scriptIds.indexOf(scriptId);
    if (i >= 0) ref.node.scriptIds.splice(i, 1);
    else ref.node.scriptIds.push(scriptId);
  }

  setSpritesheet(id: string, config: SpritesheetConfig | null) {
    const ref = this.findNode(id);
    if (ref) ref.node.spritesheet = config;
  }

  updateMeta(patch: Partial<NodeWorldMeta>) {
    Object.assign(this.meta, patch);
  }

  loadGraph(roots: SceneNode[], meta?: Partial<NodeWorldMeta>) {
    this.roots = roots;
    if (meta) Object.assign(this.meta, meta);
    this.selectedId = null;
  }

  clear() {
    this.roots = [];
    this.selectedId = null;
  }
}

export const nodeWorldStore = new NodeWorldStore();
