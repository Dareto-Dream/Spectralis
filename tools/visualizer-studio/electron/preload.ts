import { contextBridge, webUtils } from 'electron';

// node:url's pathToFileURL isn't reliably present in the sandboxed preload's
// Node polyfill (confirmed: threw "pathToFileURL is not a function" at
// runtime here even though it typechecks fine) — plain string handling avoids
// depending on it at all. Windows drive-letter paths need the extra leading
// slash after the scheme (file:///C:/...); POSIX paths already start with one.
function toFileUrl(filePath: string): string {
  const posix = filePath.replace(/\\/g, '/');
  const withLeadingSlash = posix.startsWith('/') ? posix : `/${posix}`;
  return 'file://' + encodeURI(withLeadingSlash).replace(/#/g, '%23');
}

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
  toFileUrl,
});
