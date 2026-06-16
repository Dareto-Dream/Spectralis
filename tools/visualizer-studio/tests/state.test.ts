import { describe, expect, it } from 'vitest';
import { HistoryStack } from '../src/state/history.svelte';
import { ProjectStore } from '../src/state/project.svelte';
import { ToastStore } from '../src/state/toast.svelte';
import { AutosaveManager } from '../src/state/autosave.svelte';
import type { Project } from '../src/types/project';

function blankProject(): Project {
  return {
    formatVersion: 2,
    meta: { title: 'T', artist: '', slug: 't', songEnd: 60, aspect: '9x16' },
    sections: [{ id: 's1', label: 'S1', start: 0, end: 60, hue: 0, hue2: 0, intensity: 0.5, noLyrics: false }],
    layers: [],
  };
}

describe('HistoryStack', () => {
  // reset(initial) seeds the undo floor, mirroring loadProject() — a lone commit()
  // with no prior baseline has nothing to diff against yet (nothing to undo TO).
  const projectTitled = (title: string) => ({ ...blankProject(), meta: { ...blankProject().meta, title } });

  it('a commit with no prior baseline at all (never reset) has nothing to undo to', () => {
    const h = new HistoryStack();
    h.commit(projectTitled('A'));
    expect(h.canUndo).toBe(false);
  });

  it('reset(initial) seeds the undo floor, so the very next commit IS undoable', () => {
    const h = new HistoryStack();
    h.reset(projectTitled('A'));
    h.commit(projectTitled('B'));
    expect(h.canUndo).toBe(true);
  });

  it('undo returns the state BEFORE the last commit, not the state at the last commit', () => {
    const h = new HistoryStack();
    h.reset(projectTitled('A'));
    h.commit(projectTitled('B'));
    const prev = h.undo(projectTitled('B'));
    expect(prev?.meta.title).toBe('A');
    expect(h.canRedo).toBe(true);
  });

  it('redo returns to the state that was undone', () => {
    const h = new HistoryStack();
    h.reset(projectTitled('A'));
    h.commit(projectTitled('B'));
    h.undo(projectTitled('B'));
    const next = h.redo(projectTitled('A'));
    expect(next?.meta.title).toBe('B');
  });

  it('redo is cleared by a new commit (no stale forward history after a fresh edit)', () => {
    const h = new HistoryStack();
    h.reset(projectTitled('A'));
    h.commit(projectTitled('B'));
    h.undo(projectTitled('B'));
    expect(h.canRedo).toBe(true);
    h.commit(projectTitled('C'));
    expect(h.canRedo).toBe(false);
  });

  it('undo/redo on an empty stack is a no-op that returns null', () => {
    const h = new HistoryStack();
    expect(h.undo(blankProject())).toBeNull();
    expect(h.redo(blankProject())).toBeNull();
  });

  it('reset clears both stacks (used on loadProject)', () => {
    const h = new HistoryStack();
    h.reset(projectTitled('A'));
    h.commit(projectTitled('B'));
    h.reset();
    expect(h.canUndo).toBe(false);
    expect(h.hasUncommittedSinceLoad).toBe(false);
  });
});

describe('ProjectStore — layers', () => {
  it('addLayer selects the new layer and commits one history entry', () => {
    const store = new ProjectStore();
    store.loadProject(blankProject());
    const layer = store.addLayer('orb');
    expect(store.project.layers).toHaveLength(1);
    expect(store.selection.layerId).toBe(layer.id);
    expect(store.history.canUndo).toBe(true);
  });

  it('undo after addLayer restores the empty layer list', () => {
    const store = new ProjectStore();
    store.loadProject(blankProject());
    store.addLayer('orb');
    store.undo();
    expect(store.project.layers).toHaveLength(0);
  });

  it('duplicateLayer gives the clone fresh layer and keyframe ids', () => {
    const store = new ProjectStore();
    store.loadProject(blankProject());
    const layer = store.addLayer('orb');
    store.toggleKeyframing(layer.id, 'opacity');
    const clone = store.duplicateLayer(layer.id)!;
    expect(clone.id).not.toBe(layer.id);
    expect(clone.tracks.opacity[0].id).not.toBe(layer.tracks.opacity[0].id);
    expect(clone.name).toBe('Orb copy');
  });

  it('moveLayer swaps stacking order and no-ops at the array edges', () => {
    const store = new ProjectStore();
    store.loadProject(blankProject());
    const a = store.addLayer('orb');
    const b = store.addLayer('ring');
    store.moveLayer(b.id, -1);
    expect(store.project.layers.map((l) => l.id)).toEqual([b.id, a.id]);
    store.moveLayer(b.id, -1); // already at index 0 — no-op
    expect(store.project.layers.map((l) => l.id)).toEqual([b.id, a.id]);
  });
});

describe('ProjectStore — keyframing', () => {
  it('toggleKeyframing seeds one keyframe at t:0 holding the static value', () => {
    const store = new ProjectStore();
    store.loadProject(blankProject());
    const layer = store.addLayer('orb');
    store.toggleKeyframing(layer.id, 'opacity');
    expect(layer.tracks.opacity).toEqual([{ id: expect.any(String), t: 0, v: 1, ease: 'linear' }]);
  });

  it('toggling keyframing off folds the current playhead value back into statics', () => {
    const store = new ProjectStore();
    store.loadProject(blankProject());
    const layer = store.addLayer('orb');
    store.toggleKeyframing(layer.id, 'scale');
    store.addKeyframeAtPlayhead(layer.id, 'scale'); // t:0 already exists — this adds nothing new at t=0
    layer.tracks.scale = [
      { id: 'a', t: 0, v: 0, ease: 'linear' },
      { id: 'b', t: 10, v: 2, ease: 'linear' },
    ];
    store.seekTo(5);
    store.toggleKeyframing(layer.id, 'scale');
    expect(layer.tracks.scale).toEqual([]);
    expect(layer.statics.scale).toBe(1); // midpoint of 0->2 linear at t=5
  });

  it('addKeyframeAtPlayhead inserts the currently-evaluated value, not a stale one', () => {
    const store = new ProjectStore();
    store.loadProject(blankProject());
    const layer = store.addLayer('orb');
    layer.tracks.scale = [
      { id: 'a', t: 0, v: 0, ease: 'linear' },
      { id: 'b', t: 10, v: 10, ease: 'linear' },
    ];
    store.seekTo(4);
    const id = store.addKeyframeAtPlayhead(layer.id, 'scale')!;
    const inserted = layer.tracks.scale.find((k) => k.id === id)!;
    expect(inserted.t).toBe(4);
    expect(inserted.v).toBe(4);
    // stays sorted by time
    expect(layer.tracks.scale.map((k) => k.t)).toEqual([0, 4, 10]);
  });

  it('deleteKeyframe removes it from the track and from selection', () => {
    const store = new ProjectStore();
    store.loadProject(blankProject());
    const layer = store.addLayer('orb');
    store.toggleKeyframing(layer.id, 'opacity');
    const id = layer.tracks.opacity[0].id;
    store.selection.selectKeyframe(id);
    store.deleteKeyframe(id);
    expect(layer.tracks.opacity).toHaveLength(0);
    expect(store.selection.keyframeIds.has(id)).toBe(false);
  });
});

describe('ProjectStore — sections', () => {
  it('deleteSection refuses to remove the last remaining section', () => {
    const store = new ProjectStore();
    store.loadProject(blankProject());
    store.deleteSection('s1');
    expect(store.project.sections).toHaveLength(1);
  });

  it('updateSection self-corrects an inverted end<start instead of accepting it', () => {
    const store = new ProjectStore();
    store.loadProject(blankProject());
    store.updateSection('s1', { start: 20, end: 10 });
    const sec = store.project.sections[0];
    expect(sec.end).toBeGreaterThan(sec.start);
  });
});

describe('ToastStore', () => {
  it('push adds a toast and dismiss removes it', () => {
    const t = new ToastStore();
    const id = t.push('success', 'saved');
    expect(t.toasts).toHaveLength(1);
    t.dismiss(id);
    expect(t.toasts).toHaveLength(0);
  });
});

describe('AutosaveManager', () => {
  it('round-trips a saved entry through localStorage', () => {
    const a = new AutosaveManager();
    a.saveNow({ hello: 'world' });
    const saved = a.readSaved();
    expect(saved?.project).toEqual({ hello: 'world' });
    a.clear();
    expect(a.readSaved()).toBeNull();
  });
});
