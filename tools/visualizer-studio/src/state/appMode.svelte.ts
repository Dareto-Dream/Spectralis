// Capsule and World are genuinely separate top-level modes (each saves to
// its own file format — .spex vs .spectral — and gets its own
// DockviewLayout instance), not tabs sharing one workspace the way
// Studio/World/Story used to. Module singleton, same pattern as
// dockManager/toast/confirmModal — ActionBar writes it, DockviewLayout and
// MenuBar read it.
export type AppMode = 'capsule' | 'world';

class AppModeState {
  mode: AppMode = $state('capsule');
}

export const appMode = new AppModeState();
