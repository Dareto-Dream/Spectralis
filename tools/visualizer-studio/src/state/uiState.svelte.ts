// Tiny singleton for global modal open/closed flags — lets both the menu bar
// and the keyboard shortcut handler (lib/keymap.ts's openHelp callback) reach
// the same flags without prop-drilling through App.svelte.
class UiState {
  helpOpen = $state(false);
  settingsOpen = $state(false);
  aboutOpen = $state(false);
}

export const uiState = new UiState();
