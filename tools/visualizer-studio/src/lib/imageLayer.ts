// "Add image layers... dragged onto layers" — creates a bitmap layer from an
// AssetLibrary entry (dragged from Assets onto the Workspace canvas, see
// preview/WorkspaceCanvas.svelte's assetDrop wiring). The layer is
// self-contained from the moment it's created (params.dataUrl copies the
// asset's actual bytes, per types/project.ts's LayerParamsByType.bitmap doc)
// — sourceAssetId is only a soft link back for editor-side convenience.
import type { ProjectStore } from '../state/project.svelte';
import type { AssetEntry } from '../types/asset';
import { newLayer } from '../state/factories';

// Local units, not pixels — a freshly dropped image is capped to this on its
// long edge so it doesn't dwarf the canvas by default (statics.scale/the
// Workspace transform tool can always resize it afterward).
const MAX_DIM = 200;

export function addImageLayerFromAsset(store: ProjectStore, entry: AssetEntry): void {
  const img = new Image();
  img.onload = () => {
    const nw = img.naturalWidth || MAX_DIM;
    const nh = img.naturalHeight || MAX_DIM;
    const fit = Math.min(1, MAX_DIM / Math.max(nw, nh));
    const layer = newLayer('bitmap');
    layer.name = entry.name;
    layer.params = { dataUrl: entry.dataUrl, sourceAssetId: entry.id, w: nw * fit, h: nh * fit };
    store.project.layers.push(layer);
    const reactive = store.project.layers[store.project.layers.length - 1];
    store.selection.selectLayer(reactive.id);
    store.commit();
  };
  img.src = entry.dataUrl;
}
