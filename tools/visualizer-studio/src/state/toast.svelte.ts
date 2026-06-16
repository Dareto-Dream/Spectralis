export type ToastKind = 'info' | 'success' | 'error';

export interface Toast {
  id: string;
  kind: ToastKind;
  text: string;
}

export class ToastStore {
  toasts: Toast[] = $state([]);

  push(kind: ToastKind, text: string, ttlMs = 3200): string {
    const id = crypto.randomUUID();
    this.toasts.push({ id, kind, text });
    setTimeout(() => this.dismiss(id), ttlMs);
    return id;
  }

  dismiss(id: string) {
    this.toasts = this.toasts.filter((t) => t.id !== id);
  }
}

export const toast = new ToastStore();
