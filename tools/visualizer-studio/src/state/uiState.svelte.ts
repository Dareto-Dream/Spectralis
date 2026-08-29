// Tiny singleton for global modal open/closed flags — lets both the menu bar
// and the keyboard shortcut handler (lib/keymap.ts's openHelp callback) reach
// the same flags without prop-drilling through App.svelte.
class UiState {
  helpOpen = $state(false);
  settingsOpen = $state(false);
  aboutOpen = $state(false);
  // Which script asset is open in ScriptEditorModal — null means closed.
  // A single id rather than a boolean since the modal needs to know WHICH
  // asset it's editing, same shape as e.g. a "currently open document".
  editingScriptAssetId: string | null = $state(null);
  // Replaces the old raw "LRC Lyrics…" file-input flow (lib/lrcImport.ts,
  // removed — it wrote straight onto a `lyrics`-kind layer that no longer
  // exists). LyricsImporter.svelte does the parse/settings/preview/commit.
  lyricsImporterOpen = $state(false);
}

export const uiState = new UiState();
