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

  commit(project: Project) {
    const snapshot = structuredClone($state.snapshot(project)) as Project;
    if (this.baseline) {
      this.undoStack.push(this.baseline);
      if (this.undoStack.length > LIMIT) this.undoStack.shift();
    }
    this.baseline = snapshot;
    this.redoStack = [];
  }

  undo(current: Project): Project | null {
    const prev = this.undoStack.pop();
    if (!prev) return null;
    this.redoStack.push(structuredClone($state.snapshot(current)) as Project);
    this.baseline = prev;
    return prev;
  }

  redo(current: Project): Project | null {
    const next = this.redoStack.pop();
    if (!next) return null;
    this.undoStack.push(structuredClone($state.snapshot(current)) as Project);
    this.baseline = next;
    return next;
  }

  // Called from loadProject() — `initial` becomes the new undo floor so the
  // first edit after a load has the loaded state to diff against.
  reset(initial?: Project) {
    this.undoStack = [];
    this.redoStack = [];
    this.baseline = initial ? (structuredClone($state.snapshot(initial)) as Project) : null;
  }

  get canUndo(): boolean {
    return this.undoStack.length > 0;
  }

  get canRedo(): boolean {
    return this.redoStack.length > 0;
  }

  // QoL autosave/beforeunload/unsaved-changes-confirm all key off this: true once
  // anything has been committed since the last load/reset.
  get hasUncommittedSinceLoad(): boolean {
    return this.undoStack.length > 0;
  }
}
