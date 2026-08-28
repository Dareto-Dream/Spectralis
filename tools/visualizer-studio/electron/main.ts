import { app, BrowserWindow, Menu } from 'electron';
import path from 'node:path';

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

app.whenReady().then(() => {
  createWindow();
  app.on('activate', () => {
    if (BrowserWindow.getAllWindows().length === 0) createWindow();
  });
});

app.on('window-all-closed', () => {
  if (process.platform !== 'darwin') app.quit();
});
