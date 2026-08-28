export interface CoverImage {
  file: File;
  dataUrl: string;
  // Electron build only — see AssetEntry.path in types/asset.ts.
  path?: string;
}

// Gap 2 support: cover image flow-through. No cover attached is identical to
// today's already-correct empty-state export behavior.
export class AssetsState {
  coverImage: CoverImage | null = $state(null);

  async loadCover(file: File) {
    const path = window.native?.getPathForFile(file);
    const dataUrl = path ? window.native!.toFileUrl(path) : await new Promise<string>((resolve, reject) => {
      const reader = new FileReader();
      reader.onload = () => resolve(reader.result as string);
      reader.onerror = () => reject(reader.error);
      reader.readAsDataURL(file);
    });
    this.coverImage = { file, dataUrl, path };
  }

  // Godot-style asset swapping: an asset dragged in from the Assets docker
  // already has a dataUrl (and, in the native build, a path), so this skips
  // the FileReader round-trip loadCover() needs for a raw drag-drop File. The
  // export pipeline only ever reads coverImage.dataUrl, coverImage.path, and
  // coverImage.file.name (for the extension) — never the file's actual bytes
  // via the File object itself, so an empty synthetic File is a safe stand-in.
  setCoverFromAsset(name: string, dataUrl: string, path?: string) {
    this.coverImage = { file: new File([], name), dataUrl, path };
  }

  clearCover() {
    this.coverImage = null;
  }

  get extension(): string {
    if (!this.coverImage) return 'png';
    const name = this.coverImage.file.name;
    const dot = name.lastIndexOf('.');
    return dot >= 0 ? name.slice(dot + 1).toLowerCase() : 'png';
  }
}
