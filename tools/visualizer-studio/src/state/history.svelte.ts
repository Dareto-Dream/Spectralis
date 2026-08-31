import type { Project } from '../types/project';

const LIMIT = 200;

// Structural-snapshot undo/redo (not a command pattern) — project sizes are small
// enough that structuredClone is sub-millisecond, and this automatically covers
// every mutation surface without a hand-written inverse for each one.
//
// Store methods mutate `project` in place and then call `commit(project)` —
// commit() has to push the state as it was BEFORE that mutation, not after, or
// undo() just hands back the same post-mutation state. `baseline` is that
// pre-mutation snapshot, updated on every commit/undo/redo/reset so each
// "mutate, then commit()" call always has something correct to diff against.
export class HistoryStack {
  private baseline: Project | null = null;

  undoStack: Project[] = $state([]);
  redoStack: Project[] = $state([]);

  // Separate from undoStack.length on purpose — that only ever GROWS across
  // a save (undo/redo don't shrink what's undoable), so keying the "unsaved
  // changes" prompt off it directly meant saving never actually cleared the
  // prompt: it stayed stuck on for the rest of the session after the first
  // edit, even seconds after a successful Save with nothing touched since.
  // This tracks "since the last save (or load, if never saved)" instead.
  private dirty = $state(false);

  commit(project: Project) {
    const snapshot = structuredClone($state.snapshot(project)) as Project;
    if (this.baseline) {
      this.undoStack.push(this.baseline);
      if (this.undoStack.length > LIMIT) this.undoStack.shift();
    }
    this.baseline = snapshot;
    this.redoStack = [];
    this.dirty = true;
  }

  undo(current: Project): Project | null {
    const prev = this.undoStack.pop();
    if (!prev) return null;
    this.redoStack.push(structuredClone($state.snapshot(current)) as Project);
    this.baseline = prev;
    this.dirty = true;
    return prev;
  }

  redo(current: Project): Project | null {
    const next = this.redoStack.pop();
    if (!next) return null;
    this.undoStack.push(structuredClone($state.snapshot(current)) as Project);
    this.baseline = next;
    this.dirty = true;
    return next;
  }

  // Called from loadProject() — `initial` becomes the new undo floor so the
  // first edit after a load has the loaded state to diff against. A freshly
  // loaded project exactly matches what's on disk (or is a blank new one
  // with nothing to lose either way), so this is also the other place dirty
  // goes back to false.
  reset(initial?: Project) {
    this.undoStack = [];
    this.redoStack = [];
    this.baseline = initial ? (structuredClone($state.snapshot(initial)) as Project) : null;
    this.dirty = false;
  }

  // Called after a successful Save/Save As (lib/projectSave.ts) — the
  // current state is now exactly what's on disk, so nothing here is
  // "unsaved" anymore even though undo history is deliberately left intact
  // (you can still undo past a save, same as every real editor).
  markSaved() {
    this.dirty = false;
  }

  get canUndo(): boolean {
    return this.undoStack.length > 0;
  }

  get canRedo(): boolean {
    return this.redoStack.length > 0;
  }

  // QoL autosave/beforeunload/unsaved-changes-confirm all key off this: true
  // once anything has changed since the last save (or since load, if this
  // session hasn't saved yet).
  get hasUncommittedSinceLoad(): boolean {
    return this.dirty;
  }
}
