/// <reference types="vite/client" />

// Present only in the Electron build (exposed by electron/preload.ts) —
// undefined in the plain browser build. Every consumer must feature-detect.
interface Window {
  native?: {
    platform: string;
    getPathForFile(file: File): string;
    toFileUrl(path: string): string;
    sha256File(path: string): Promise<string>;
  };
}
