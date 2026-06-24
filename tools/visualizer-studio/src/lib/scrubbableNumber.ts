// The single most recognizable AE-specific interaction (plan QoL §I): click-
// drag horizontally on a numeric field's LABEL (not the input itself, so text
// selection/typing in the input is never fought with) scrubs its value.
// Shift = coarse step (x10), Alt = fine step (x0.1) — held at drag time, not
// fixed at drag start, so the modifier can change mid-drag like in AE.
//
// Fires `onChange` live on every pointermove (for a responsive preview) and
// `onCommit` exactly once on pointerup — callers wire onChange to a plain
// mutation and onCommit to `store.commit()`, matching the app's existing
// "continuous actions commit once" rule. A drag that never moves past the
// threshold is treated as a plain click and fires `onClick` instead (so the
// label can still do its existing job, e.g. selecting a track) rather than a
// zero-length scrub.
export interface ScrubbableOptions {
  value: number;
  step?: number;
  min?: number;
  max?: number;
  onChange: (v: number) => void;
  onCommit: () => void;
  onClick?: () => void;
}

const MOVE_THRESHOLD_PX = 3;
const PX_PER_STEP = 4; // drag this many px to move one `step`

export function scrubbable(node: HTMLElement, opts: ScrubbableOptions) {
  let current = opts;
  let dragging = false;
  let moved = false;
  let startX = 0;
  let startValue = 0;
  let pointerId: number | null = null;

  function clamp(v: number): number {
    if (current.min !== undefined) v = Math.max(current.min, v);
    if (current.max !== undefined) v = Math.min(current.max, v);
    return v;
  }

  function onPointerDown(e: PointerEvent) {
    if (e.button !== 0) return;
    dragging = true;
    moved = false;
    startX = e.clientX;
    startValue = current.value;
    pointerId = e.pointerId;
    node.setPointerCapture(e.pointerId);
    window.addEventListener('pointermove', onPointerMove);
    window.addEventListener('pointerup', onPointerUp);
  }

  function onPointerMove(e: PointerEvent) {
    if (!dragging) return;
    const dx = e.clientX - startX;
    if (!moved && Math.abs(dx) < MOVE_THRESHOLD_PX) return;
    moved = true;
    node.classList.add('scrubbing');
    const base = current.step ?? 1;
    const mult = e.shiftKey ? 10 : e.altKey ? 0.1 : 1;
    const next = clamp(startValue + Math.round(dx / PX_PER_STEP) * base * mult);
    current.onChange(next);
  }

  function onPointerUp(e: PointerEvent) {
    if (!dragging) return;
    dragging = false;
    node.classList.remove('scrubbing');
    if (pointerId !== null) {
      try {
        node.releasePointerCapture(pointerId);
      } catch {
        // already released (e.g. pointer left the window) — fine to ignore.
      }
    }
    window.removeEventListener('pointermove', onPointerMove);
    window.removeEventListener('pointerup', onPointerUp);
    if (moved) current.onCommit();
    else current.onClick?.();
  }

  node.classList.add('scrubbable');
  node.addEventListener('pointerdown', onPointerDown);

  return {
    update(newOpts: ScrubbableOptions) {
      current = newOpts;
    },
    destroy() {
      node.removeEventListener('pointerdown', onPointerDown);
      window.removeEventListener('pointermove', onPointerMove);
      window.removeEventListener('pointerup', onPointerUp);
    },
  };
}
