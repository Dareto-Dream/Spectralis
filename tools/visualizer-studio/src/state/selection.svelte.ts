import type { AnimKey, AnyLayer, Project } from '../types/project';

export interface KeyframeRef {
  layer: AnyLayer;
  trackKey: AnimKey;
  index: number;
}

// Keyframes are addressed by stable id, not array index — indices shift under
// sort/insert/delete once multi-select/marquee/paste are in play.
export class SelectionState {
  private getProject: () => Project;

  layerId: string | null = $state(null);
  trackKey: AnimKey | null = $state(null);
  keyframeIds: Set<string> = $state(new Set());
  sectionId: string | null = $state(null);

  // Bumped by the global F2 shortcut; LayerRow watches this + its own layer id
  // to start its existing inline-rename mode without the keymap needing to
  // reach into a specific row component directly.
  renameRequestId = $state(0);
  requestRename() {
    this.renameRequestId++;
  }

  constructor(getProject: () => Project) {
    this.getProject = getProject;
  }

  keyframeIndex: Map<string, KeyframeRef> = $derived.by(() => {
    const map = new Map<string, KeyframeRef>();
    for (const layer of this.getProject().layers) {
      for (const trackKey of Object.keys(layer.tracks) as AnimKey[]) {
        layer.tracks[trackKey].forEach((kf, index) => {
          map.set(kf.id, { layer, trackKey, index });
        });
      }
    }
    return map;
  });

  get selectedLayer(): AnyLayer | null {
    return this.getProject().layers.find((l) => l.id === this.layerId) ?? null;
  }

  selectLayer(id: string | null) {
    this.layerId = id;
    this.trackKey = null;
    this.keyframeIds = new Set();
  }

  selectTrack(key: AnimKey | null) {
    this.trackKey = key;
  }

  selectKeyframe(id: string, opts: { additive?: boolean } = {}) {
    if (opts.additive) {
      const next = new Set(this.keyframeIds);
      if (next.has(id)) next.delete(id);
      else next.add(id);
      this.keyframeIds = next;
    } else {
      this.keyframeIds = new Set([id]);
    }
  }

  setKeyframeSelection(ids: Iterable<string>) {
    this.keyframeIds = new Set(ids);
  }

  clearKeyframes() {
    this.keyframeIds = new Set();
  }

  selectSection(id: string | null) {
    this.sectionId = id;
  }

  clearAll() {
    this.layerId = null;
    this.trackKey = null;
    this.keyframeIds = new Set();
    this.sectionId = null;
  }
}
