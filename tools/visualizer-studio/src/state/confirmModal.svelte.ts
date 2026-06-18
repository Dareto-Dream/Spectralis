export interface ConfirmOptions {
  title: string;
  body: string;
  confirmLabel?: string;
  danger?: boolean;
}

interface ConfirmRequest extends ConfirmOptions {
  resolve: (value: boolean) => void;
}

class ConfirmModalState {
  request: ConfirmRequest | null = $state(null);
}

export const confirmModalState = new ConfirmModalState();

// Promise-returning helper so call sites read as `if (!await confirmDialog({...})) return;`
// instead of callback soup. Only one confirm can be pending at a time — a second
// call before the first resolves replaces it (matches how a real modal would behave).
export function confirmDialog(opts: ConfirmOptions): Promise<boolean> {
  return new Promise((resolve) => {
    confirmModalState.request = { ...opts, resolve };
  });
}
