// Shared "Assets" docker (plan follow-up: Godot-style project asset browser).
// Session-only by design — see state/assetLibrary.svelte.ts for why these
// aren't persisted to localStorage the way project autosave/layout are.
export type AssetKind = 'image' | 'audio' | 'svg';

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
