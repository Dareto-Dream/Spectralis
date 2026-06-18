export interface ContextMenuItem {
  label: string;
  action: () => void;
  danger?: boolean;
  disabled?: boolean;
}

interface ContextMenuRequest {
  x: number;
  y: number;
  items: ContextMenuItem[];
}

class ContextMenuState {
  request: ContextMenuRequest | null = $state(null);
}

// Generic — fed by whoever opens it: a layer row, a keyframe, a section block.
export const contextMenuState = new ContextMenuState();

export function openContextMenu(x: number, y: number, items: ContextMenuItem[]) {
  contextMenuState.request = { x, y, items };
}

export function closeContextMenu() {
  contextMenuState.request = null;
}
