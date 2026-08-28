// Shared "Assets" docker (plan follow-up: Godot-style project asset browser).
// Session-only by design — see state/assetLibrary.svelte.ts for why these
// aren't persisted to localStorage the way project autosave/layout are.
// 'script' backs World node scripts (Spectralis Code / .spc) — plain JS with
// a small `node`/`on` API, see core/nodeRender.js. Scripts live in the Assets
// docker like any other asset and are only ever REFERENCED by id from a
// node's scriptIds, never embedded — "scripts get attached to nodes, they
// are not immediately associated."
export type AssetKind = 'image' | 'audio' | 'svg' | 'script';

export interface AssetEntry {
  id: string;
  name: string;
  kind: AssetKind;
  mime: string;
  dataUrl: string;
  size: number;
  createdAt: number;
  // Electron build only — real fs path, set when this entry came from a File
  // window.native could resolve (see assetLibrary.svelte.ts). Lets the export
  // pipeline fs.copyFile the real bytes instead of re-reading a data: URL.
  path?: string;
}

// The custom drag mime used to move an asset FROM the Assets docker onto a
// drop target elsewhere in the app (cover slot, World track fields, ...) —
// distinct from a real OS file drag, which lib/dropZone.ts already owns.
export const ASSET_DRAG_MIME = 'application/x-vstudio-asset-id';
