export interface CoverImage {
  file: File;
  dataUrl: string;
}

// Gap 2 support: cover image flow-through. No cover attached is identical to
// today's already-correct empty-state export behavior.
export class AssetsState {
  coverImage: CoverImage | null = $state(null);

  async loadCover(file: File) {
    const dataUrl = await new Promise<string>((resolve, reject) => {
      const reader = new FileReader();
      reader.onload = () => resolve(reader.result as string);
      reader.onerror = () => reject(reader.error);
      reader.readAsDataURL(file);
    });
    this.coverImage = { file, dataUrl };
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
