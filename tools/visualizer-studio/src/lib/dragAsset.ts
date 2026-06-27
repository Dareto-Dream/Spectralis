// Internal app-to-app drag-and-drop: moving an AssetEntry card out of the
// Assets docker onto a drop target elsewhere (cover slot, World track row,
// Story portrait, ...). Deliberately separate from lib/dropZone.ts, which
// only reacts to real OS file drags ('Files' in dataTransfer.types) — an
// asset-card drag carries no Files entry, so the two actions can sit on the
// same element without either one accidentally swallowing the other's drop.
import { ASSET_DRAG_MIME } from '../types/asset';
import { assetLibrary } from '../state/assetLibrary.svelte';
import type { AssetEntry } from '../types/asset';

export function startAssetDrag(e: DragEvent, entry: AssetEntry) {
  if (!e.dataTransfer) return;
  e.dataTransfer.effectAllowed = 'copy';
  e.dataTransfer.setData(ASSET_DRAG_MIME, entry.id);
  // Fallback so dropping onto a plain text input still inserts something
  // sensible via the browser's own default drop-into-input behavior.
  e.dataTransfer.setData('text/plain', entry.name);
}

function hasAsset(e: DragEvent): boolean {
  return Array.from(e.dataTransfer?.types ?? []).includes(ASSET_DRAG_MIME);
}

export interface AssetDropOptions {
  accept?: (entry: AssetEntry) => boolean;
  onAsset: (entry: AssetEntry) => void;
}

export function assetDrop(node: HTMLElement, opts: AssetDropOptions) {
  let current = opts;
  let depth = 0;

  function onDragEnter(e: DragEvent) {
    if (!hasAsset(e)) return;
    e.preventDefault();
    depth++;
    node.classList.add('dragOver');
  }
  function onDragOver(e: DragEvent) {
    if (!hasAsset(e)) return;
    e.preventDefault();
  }
  function onDragLeave() {
    depth = Math.max(0, depth - 1);
    if (depth === 0) node.classList.remove('dragOver');
  }
  function onDrop(e: DragEvent) {
    if (!hasAsset(e)) return;
    e.preventDefault();
    depth = 0;
    node.classList.remove('dragOver');
    const id = e.dataTransfer?.getData(ASSET_DRAG_MIME);
    const entry = id ? assetLibrary.get(id) : undefined;
    if (!entry) return;
    if (!current.accept || current.accept(entry)) current.onAsset(entry);
  }

  node.addEventListener('dragenter', onDragEnter);
  node.addEventListener('dragover', onDragOver);
  node.addEventListener('dragleave', onDragLeave);
  node.addEventListener('drop', onDrop);

  return {
    update(newOpts: AssetDropOptions) {
      current = newOpts;
    },
    destroy() {
      node.removeEventListener('dragenter', onDragEnter);
      node.removeEventListener('dragover', onDragOver);
      node.removeEventListener('dragleave', onDragLeave);
      node.removeEventListener('drop', onDrop);
    },
  };
}
