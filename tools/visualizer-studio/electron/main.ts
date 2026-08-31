import { app, BrowserWindow, Menu, ipcMain, dialog } from 'electron';
import path from 'node:path';
import crypto from 'node:crypto';
import fs from 'node:fs';

// MenuBar.svelte already owns File/Edit/View/Tools/Help — a native menu bar
// on top of that would be a confusing duplicate. (Known risk, not yet
// verified on real mac hardware: this can break Cmd+C/V/X/A in text inputs
// there, since Chromium's default text-field clipboard shortcuts route
// through Edit-menu accelerators. If confirmed, swap in
// Menu.buildFromTemplate([{role:'appMenu'},{role:'editMenu'}]) instead.)
Menu.setApplicationMenu(null);

function createWindow() {
  const devUrl = process.env.VITE_DEV_SERVER_URL;
  const win = new BrowserWindow({
    width: 1400,
    height: 900,
    backgroundColor: '#0a0a0d', // matches --bg0, avoids a white flash on first paint
    webPreferences: {
      preload: path.join(__dirname, 'preload.js'),
      // electron:dev serves the renderer from http://localhost:5173 (Vite),
      // but audio/video loaded via the Assets panel resolve to a real
      // file:// URL (preload.ts's toFileUrl) — Chromium refuses to fetch a
      // file:// resource from an http(s):// origin, so <audio>.play() would
      // silently reject and the transport driver's catch flips playing back
      // off (looks exactly like "the play button doesn't work" once real
      // audio is loaded, though everything ELSE about the load — waveform,
      // sha256, the loaded/filename UI — reads straight off the File object
      // and never touches this). A packaged build (electron:build) loads
      // dist/index.html over file:// too, so the origins already match there
      // and this only ever relaxes anything in dev.
      webSecurity: !devUrl,
    },
  });

  if (devUrl) {
    win.loadURL(devUrl);
    win.webContents.openDevTools();
  } else {
    win.loadFile(path.join(__dirname, '../dist/index.html'));
  }

  // App.svelte's onBeforeUnload calls preventDefault() when there are
  // uncommitted changes — in a real browser that pops the native "leave
  // site?" confirmation, but Electron just silently blocks the close with
  // no dialog at all, which is exactly why the close button/Alt+F4 stopped
  // doing anything. This is the documented fix: electron fires
  // will-prevent-unload instead of showing anything itself, and it's on us
  // to ask and then call event.preventDefault() to actually let it close.
  win.webContents.on('will-prevent-unload', (event) => {
    const choice = dialog.showMessageBoxSync(win, {
      type: 'question',
      buttons: ['Quit Without Saving', 'Cancel'],
      defaultId: 1,
      cancelId: 1,
      title: 'Unsaved changes',
      message: 'You have unsaved changes in Visualizer Studio.',
      detail: 'Quit anyway? Autosave keeps a recovery copy, but Save Project is safer.',
    });
    if (choice === 0) event.preventDefault();
  });
}

// Streamed over fs.createReadStream so the whole audio file is never resident
// as one buffer anywhere, in either process — the renderer-side equivalent
// (sha256Hex over a full arrayBuffer()) is exactly the RAM cost this exists
// to avoid for what can be a tens-of-MB file.
ipcMain.handle('hash:sha256File', (_event, filePath: string) => {
  return new Promise<string>((resolve, reject) => {
    const hash = crypto.createHash('sha256');
    const stream = fs.createReadStream(filePath);
    stream.on('data', (chunk) => hash.update(chunk));
    stream.on('end', () => resolve(hash.digest('hex')));
    stream.on('error', reject);
  });
});

// Documents/<productName>/{Projects,Assets,Templates,Scripts} — created once
// on launch (recursive mkdir is a no-op if it's already there) so native
// Save/Open dialogs have somewhere sensible to default into. Browser build
// has no equivalent — window.native is undefined there, same feature-detect
// as every other native-only path in this codebase.
const STUDIO_ROOT = path.join(app.getPath('documents'), 'Spectralis Visualizer Studio');
const STUDIO_SUBFOLDERS = ['Projects', 'Assets', 'Templates', 'Scripts', 'Autosaves'];

async function ensureStudioScaffold() {
  for (const sub of STUDIO_SUBFOLDERS) {
    await fs.promises.mkdir(path.join(STUDIO_ROOT, sub), { recursive: true });
  }
}

ipcMain.handle('paths:studioRoot', async () => {
  await ensureStudioScaffold();
  return STUDIO_ROOT;
});

interface DialogFilter {
  name: string;
  extensions: string[];
}

// Generic save/open dialogs + binary read/write, used by the .spex/
// .spectral project format (native path — the browser build falls back to
// Blob download / <input type=file>, see src/lib/downloadText.ts).
ipcMain.handle('dialog:saveFile', async (_event, opts: { defaultPath: string; filters: DialogFilter[] }) => {
  const win = BrowserWindow.getFocusedWindow() ?? undefined;
  const result = win
    ? await dialog.showSaveDialog(win, { defaultPath: opts.defaultPath, filters: opts.filters })
    : await dialog.showSaveDialog({ defaultPath: opts.defaultPath, filters: opts.filters });
  return result.canceled || !result.filePath ? null : result.filePath;
});

ipcMain.handle('dialog:openFile', async (_event, opts: { defaultPath?: string; filters: DialogFilter[] }) => {
  const win = BrowserWindow.getFocusedWindow() ?? undefined;
  const dialogOpts = { defaultPath: opts.defaultPath, filters: opts.filters, properties: ['openFile'] as Array<'openFile'> };
  const result = win ? await dialog.showOpenDialog(win, dialogOpts) : await dialog.showOpenDialog(dialogOpts);
  return result.canceled || !result.filePaths[0] ? null : result.filePaths[0];
});

ipcMain.handle('fs:writeBinary', async (_event, filePath: string, data: ArrayBuffer) => {
  await fs.promises.mkdir(path.dirname(filePath), { recursive: true });
  await fs.promises.writeFile(filePath, Buffer.from(data));
  return { ok: true };
});

// Used by the autosave manager to remove its own rotating file once a real
// Save Project/Save World lands — `force: true` makes a missing file a no-op
// instead of throwing, since "nothing to clear" is a normal outcome here.
ipcMain.handle('fs:deleteFile', async (_event, filePath: string) => {
  await fs.promises.rm(filePath, { force: true });
  return { ok: true };
});

ipcMain.handle('fs:readBinary', async (_event, filePath: string) => {
  const buf = await fs.promises.readFile(filePath);
  // structuredClone-friendly: ipcRenderer.invoke can return a Buffer, but the
  // renderer sees it as a plain Uint8Array-shaped object over IPC anyway —
  // returning the underlying ArrayBuffer slice keeps the renderer-side type
  // (Uint8Array(bytes.buffer)) unambiguous.
  return buf.buffer.slice(buf.byteOffset, buf.byteOffset + buf.byteLength);
});

interface WriteExportPayload {
  dir: string;
  files: { name: string; content: string }[];
  coverSourcePath: string | null;
  coverDestName: string | null;
  audioSourcePath: string | null;
  audioDestName: string | null;
}

ipcMain.handle('dialog:chooseExportDir', async (_event, defaultName: string) => {
  const win = BrowserWindow.getFocusedWindow() ?? undefined;
  const result = win
    ? await dialog.showOpenDialog(win, { properties: ['openDirectory', 'createDirectory'], title: `Export "${defaultName}" to folder` })
    : await dialog.showOpenDialog({ properties: ['openDirectory', 'createDirectory'], title: `Export "${defaultName}" to folder` });
  return result.canceled || !result.filePaths[0] ? null : result.filePaths[0];
});

// Text files land flat next to the generated pack_*.py, matching what
// buildPackScript.ts's ROOT / "filename" entries already expect — same flat
// layout the browser build's downloads always produced, just automated.
// Cover/audio bytes are fs.copyFile'd straight from their source paths,
// never read into either process's JS heap.
ipcMain.handle('export:write', async (_event, payload: WriteExportPayload) => {
  const { dir, files, coverSourcePath, coverDestName, audioSourcePath, audioDestName } = payload;
  await fs.promises.mkdir(dir, { recursive: true });
  for (const f of files) await fs.promises.writeFile(path.join(dir, f.name), f.content, 'utf-8');
  if (coverSourcePath && coverDestName) await fs.promises.copyFile(coverSourcePath, path.join(dir, coverDestName));
  if (audioSourcePath && audioDestName) await fs.promises.copyFile(audioSourcePath, path.join(dir, audioDestName));
  return { ok: true, dir };
});

app.whenReady().then(() => {
  void ensureStudioScaffold();
  createWindow();
  app.on('activate', () => {
    if (BrowserWindow.getAllWindows().length === 0) createWindow();
  });
});

app.on('window-all-closed', () => {
  if (process.platform !== 'darwin') app.quit();
});
