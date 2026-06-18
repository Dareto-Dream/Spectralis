const FOCUSABLE = 'button, [href], input, select, textarea, [tabindex]:not([tabindex="-1"])';

// Cycles Tab within `node` and restores focus to whatever was focused before the
// node appeared — used by ConfirmModal/ContextMenu/the shortcut cheat-sheet,
// the first true modal/overlay UI this tool has ever had.
export function focusTrap(node: HTMLElement) {
  function focusables(): HTMLElement[] {
    return Array.from(node.querySelectorAll<HTMLElement>(FOCUSABLE)).filter((el) => !el.hasAttribute('disabled'));
  }

  function handleKeydown(e: KeyboardEvent) {
    if (e.key !== 'Tab') return;
    const items = focusables();
    if (!items.length) return;
    const first = items[0];
    const last = items[items.length - 1];
    if (e.shiftKey && document.activeElement === first) {
      e.preventDefault();
      last.focus();
    } else if (!e.shiftKey && document.activeElement === last) {
      e.preventDefault();
      first.focus();
    }
  }

  const previouslyFocused = document.activeElement as HTMLElement | null;
  node.addEventListener('keydown', handleKeydown);
  focusables()[0]?.focus();

  return {
    destroy() {
      node.removeEventListener('keydown', handleKeydown);
      previouslyFocused?.focus();
    },
  };
}
