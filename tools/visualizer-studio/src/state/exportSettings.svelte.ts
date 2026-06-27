// sharedPlay + exporting flags, lifted out of TopBar.svelte so Tools > Render
// in the menu bar can trigger an export without needing TopBar mounted at all
// (the top bar is Studio-specific chrome; the menu bar is global).
export class ExportSettings {
  sharedPlay = $state(false);
  exporting = $state(false);
}

export const exportSettings = new ExportSettings();
