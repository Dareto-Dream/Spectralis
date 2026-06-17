import type { AnimKey, Ease } from '../types/project';

export interface ClipboardKeyframe {
  layerId: string;
  trackKey: AnimKey;
  t: number;
  v: number;
  ease: Ease;
}

interface ClipboardEntry extends ClipboardKeyframe {
  relT: number; // offset from the copy anchor (usually the playhead at copy time)
}

// Ephemeral module state — NOT undoable, NOT saved with the project. Stores both
// relT (offset from the copy anchor) and the original absolute time, so Ctrl+V
// can paste at the playhead while Ctrl+Shift+V re-pastes at the original times
// (e.g. copying a curve from one layer/track onto another at the same times).
let clipboard: ClipboardEntry[] = [];

export function copyKeyframes(entries: ClipboardKeyframe[], anchor: number) {
  clipboard = entries.map((e) => ({ ...e, relT: e.t - anchor }));
}

export function hasClipboard(): boolean {
  return clipboard.length > 0;
}

export function pasteAtPlayhead(playhead: number): ClipboardKeyframe[] {
  return clipboard.map(({ layerId, trackKey, v, ease, relT }) => ({
    layerId,
    trackKey,
    t: Math.max(0, playhead + relT),
    v,
    ease,
  }));
}

export function pasteAtOriginalTimes(): ClipboardKeyframe[] {
  return clipboard.map(({ layerId, trackKey, t, v, ease }) => ({ layerId, trackKey, t, v, ease }));
}

export function clearClipboard() {
  clipboard = [];
}
