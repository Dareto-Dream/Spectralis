import { contextBridge, ipcRenderer, webUtils } from 'electron';

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
  sha256File: (path: string) => ipcRenderer.invoke('hash:sha256File', path) as Promise<string>,
  chooseExportDir: (defaultName: string) => ipcRenderer.invoke('dialog:chooseExportDir', defaultName) as Promise<string | null>,
  writeExport: (payload: unknown) => ipcRenderer.invoke('export:write', payload) as Promise<{ ok: boolean; dir: string }>,
  getStudioRoot: () => ipcRenderer.invoke('paths:studioRoot') as Promise<string>,
  saveFileDialog: (opts: { defaultPath: string; filters: { name: string; extensions: string[] }[] }) =>
    ipcRenderer.invoke('dialog:saveFile', opts) as Promise<string | null>,
  openFileDialog: (opts: { defaultPath?: string; filters: { name: string; extensions: string[] }[] }) =>
    ipcRenderer.invoke('dialog:openFile', opts) as Promise<string | null>,
  writeBinaryFile: (filePath: string, data: ArrayBuffer) => ipcRenderer.invoke('fs:writeBinary', filePath, data) as Promise<{ ok: boolean }>,
  readBinaryFile: (filePath: string) => ipcRenderer.invoke('fs:readBinary', filePath) as Promise<ArrayBuffer>,
  deleteFile: (filePath: string) => ipcRenderer.invoke('fs:deleteFile', filePath) as Promise<{ ok: boolean }>,
});
