export type MenuNode =
  | { kind: 'action'; label: string; action: () => void; shortcut?: string; danger?: boolean; disabled?: boolean }
  | { kind: 'checkbox'; label: string; checked: boolean; action: () => void; disabled?: boolean }
  | { kind: 'submenu'; label: string; items: MenuNode[] }
  | { kind: 'separator' };

export interface TopMenu {
  label: string;
  items: MenuNode[];
}
