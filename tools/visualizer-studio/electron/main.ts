import { app, BrowserWindow, Menu, ipcMain } from 'electron';
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
  const win = new BrowserWindow({
    width: 1400,
    height: 900,
    backgroundColor: '#0a0a0d', // matches --bg0, avoids a white flash on first paint
    webPreferences: {
      preload: path.join(__dirname, 'preload.js'),
    },
  });

  const devUrl = process.env.VITE_DEV_SERVER_URL;
  if (devUrl) {
    win.loadURL(devUrl);
    win.webContents.openDevTools();
  } else {
    win.loadFile(path.join(__dirname, '../dist/index.html'));
  }
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

app.whenReady().then(() => {
  createWindow();
  app.on('activate', () => {
    if (BrowserWindow.getAllWindows().length === 0) createWindow();
  });
});

app.on('window-all-closed', () => {
  if (process.platform !== 'darwin') app.quit();
});
