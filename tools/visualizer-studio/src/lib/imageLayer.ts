// "Add image layers... dragged onto layers" — creates a bitmap layer from an
// AssetLibrary entry (dragged from Assets onto the Workspace canvas, see
// preview/WorkspaceCanvas.svelte's assetDrop wiring). The layer is
// self-contained from the moment it's created (params.dataUrl copies the
// asset's actual bytes, per types/project.ts's LayerParamsByType.bitmap doc)
// — sourceAssetId is only a soft link back for editor-side convenience.
//
// No size to compute — a bitmap layer is never "a box you define the size
// of". It always fills the whole canvas (core/render.js stretches it to the
// full W×H at scale:1), same as any normal image-editor layer; use the
// Select/Transform tool afterward if you want it smaller or repositioned.
import type { ProjectStore } from '../state/project.svelte';
import type { AssetEntry } from '../types/asset';
import { newLayer } from '../state/factories';

export function addImageLayerFromAsset(store: ProjectStore, entry: AssetEntry): void {
  const layer = newLayer('bitmap');
  layer.name = entry.name;
  layer.params = { dataUrl: entry.dataUrl, sourceAssetId: entry.id };
  store.project.layers.push(layer);
  const reactive = store.project.layers[store.project.layers.length - 1];
  store.selection.selectLayer(reactive.id);
  store.commit();
}
