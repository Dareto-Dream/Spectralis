import type { AssetEntry, AssetKind } from '../types/asset';

function kindOf(mime: string, name: string): AssetKind {
  if (mime.startsWith('image/svg') || name.toLowerCase().endsWith('.svg')) return 'svg';
  if (mime.startsWith('audio/')) return 'audio';
  return 'image';
}

function readAsDataUrl(file: File): Promise<string> {
  return new Promise((resolve, reject) => {
    const reader = new FileReader();
    reader.onload = () => resolve(reader.result as string);
    reader.onerror = () => reject(reader.error);
    reader.readAsDataURL(file);
  });
}

// The shared "Assets" docker (Godot-style project browser) backing drag-and-
// drop asset intake everywhere: the Studio cover slot, World track fields,
// Story portraits, and the SVG maker's "Save to Assets" output.
//
// Deliberately session-only, NOT persisted to localStorage like project
// autosave or panel layout are — audio files alone can be tens of MB, and
// localStorage's ~5-10MB origin quota would silently start failing well
// before a real project's asset set does. Losing the library on reload is an
// honest trade-off for a browser-only static page with no backing store;
// re-dragging a file back in is one drop, not a redo of real work.
export class AssetLibrary {
  assets: AssetEntry[] = $state([]);

  async addFile(file: File): Promise<AssetEntry> {
    // Native build: skip the base64 round-trip entirely (this is the whole
    // point — a dragged-in audio file can be tens of MB, see the comment
    // above) and point straight at the real file via a file:// URL instead.
    const path = window.native?.getPathForFile(file);
    const dataUrl = path ? window.native!.toFileUrl(path) : await readAsDataUrl(file);
    const entry: AssetEntry = {
      id: crypto.randomUUID(),
      name: file.name,
      kind: kindOf(file.type, file.name),
      mime: file.type || 'application/octet-stream',
      dataUrl,
      size: file.size,
      createdAt: Date.now(),
      path,
    };
    this.assets.push(entry);
    return entry;
  }

  addSvg(name: string, svgMarkup: string): AssetEntry {
    const dataUrl = 'data:image/svg+xml;base64,' + btoa(unescape(encodeURIComponent(svgMarkup)));
    const entry: AssetEntry = {
      id: crypto.randomUUID(),
      name: name.toLowerCase().endsWith('.svg') ? name : `${name}.svg`,
      kind: 'svg',
      mime: 'image/svg+xml',
      dataUrl,
      size: svgMarkup.length,
      createdAt: Date.now(),
    };
    this.assets.push(entry);
    return entry;
  }

  get(id: string): AssetEntry | undefined {
    return this.assets.find((a) => a.id === id);
  }

  rename(id: string, name: string) {
    const a = this.get(id);
    if (a && name.trim()) a.name = name.trim();
  }

  remove(id: string) {
    this.assets = this.assets.filter((a) => a.id !== id);
  }
}

export const assetLibrary = new AssetLibrary();
