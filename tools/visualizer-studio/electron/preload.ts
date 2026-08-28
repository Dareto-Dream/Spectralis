import { contextBridge, webUtils } from 'electron';
import { pathToFileURL } from 'node:url';

// Renderer code feature-detects `window.native` — undefined in the plain
// browser build (npm run build / npm run dev), present here. Every native
// code path in src/ must have a browser fallback alongside it.
//
// getPathForFile: webUtils.getPathForFile() superseded the old File.path
// augmentation (removed under contextIsolation) — this is the only way left
// to turn a renderer File object into a real fs path.
contextBridge.exposeInMainWorld('native', {
  platform: process.platform,
  getPathForFile: (file: File) => webUtils.getPathForFile(file),
  toFileUrl: (path: string) => pathToFileURL(path).href,
});
