/// <reference types="vite/client" />

// Present only in the Electron build (exposed by electron/preload.ts) —
// undefined in the plain browser build. Every consumer must feature-detect.
interface Window {
  native?: {
    platform: string;
    getPathForFile(file: File): string;
    toFileUrl(path: string): string;
    sha256File(path: string): Promise<string>;
    chooseExportDir(defaultName: string): Promise<string | null>;
    writeExport(payload: {
      dir: string;
      files: { name: string; content: string }[];
      coverSourcePath: string | null;
      coverDestName: string | null;
      audioSourcePath: string | null;
      audioDestName: string | null;
    }): Promise<{ ok: boolean; dir: string }>;
    getStudioRoot(): Promise<string>;
    saveFileDialog(opts: { defaultPath: string; filters: { name: string; extensions: string[] }[] }): Promise<string | null>;
    openFileDialog(opts: { defaultPath?: string; filters: { name: string; extensions: string[] }[] }): Promise<string | null>;
    writeBinaryFile(filePath: string, data: ArrayBuffer): Promise<{ ok: boolean }>;
    readBinaryFile(filePath: string): Promise<ArrayBuffer>;
  };
}
