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

  it('a commit marks it dirty, and markSaved (after a real Save) clears the dirty flag without touching undo history', () => {
    const h = new HistoryStack();
    h.reset(projectTitled('A'));
    h.commit(projectTitled('B'));
    expect(h.hasUncommittedSinceLoad).toBe(true);
    h.markSaved();
    expect(h.hasUncommittedSinceLoad).toBe(false);
    expect(h.canUndo).toBe(true); // undo still works across a save
  });

  it('a commit AFTER markSaved makes it dirty again', () => {
    const h = new HistoryStack();
    h.reset(projectTitled('A'));
    h.commit(projectTitled('B'));
    h.markSaved();
    h.commit(projectTitled('C'));
    expect(h.hasUncommittedSinceLoad).toBe(true);
  });

  it('undo/redo after a save mark it dirty again too (moving the current state away from what was saved)', () => {
    const h = new HistoryStack();
    h.reset(projectTitled('A'));
    h.commit(projectTitled('B'));
    h.markSaved();
    h.undo(projectTitled('B'));
    expect(h.hasUncommittedSinceLoad).toBe(true);
  });
});

describe('ProjectStore — knownFilePath (Ctrl+S vs. Ctrl+Shift+S)', () => {
  it('loadProject defaults knownFilePath to null, and accepts an explicit one (importProjectFile passes the resolved fs path)', () => {
    const store = new ProjectStore();
    store.loadProject(blankProject());
    expect(store.knownFilePath).toBeNull();
    store.loadProject(blankProject(), 'C:/Projects/song.spex');
    expect(store.knownFilePath).toBe('C:/Projects/song.spex');
  });

  it('newProject clears knownFilePath — a fresh project has no file yet', () => {
    const store = new ProjectStore();
    store.loadProject(blankProject(), 'C:/Projects/song.spex');
    store.newProject();
    expect(store.knownFilePath).toBeNull();
  });
});

describe('ProjectStore — layers', () => {
  it('addLayer selects the new layer and commits one history entry', () => {
    const store = new ProjectStore();
    store.loadProject(blankProject());
    const layer = store.addLayer('vector');
    expect(store.project.layers).toHaveLength(1);
    expect(store.selection.layerId).toBe(layer.id);
    expect(store.history.canUndo).toBe(true);
  });

  it('undo after addLayer restores the empty layer list', () => {
    const store = new ProjectStore();
    store.loadProject(blankProject());
    store.addLayer('vector');
    store.undo();
    expect(store.project.layers).toHaveLength(0);
  });

  it('duplicateLayer gives the clone fresh layer and keyframe ids', () => {
    const store = new ProjectStore();
    store.loadProject(blankProject());
    const layer = store.addLayer('vector');
    store.toggleKeyframing(layer.id, 'opacity');
    const clone = store.duplicateLayer(layer.id)!;
    expect(clone.id).not.toBe(layer.id);
    expect(clone.tracks.opacity[0].id).not.toBe(layer.tracks.opacity[0].id);
    expect(clone.name).toBe('Vector Layer copy');
  });

  it('moveLayer swaps stacking order and no-ops at the array edges', () => {
    const store = new ProjectStore();
    store.loadProject(blankProject());
    const a = store.addLayer('vector');
    const b = store.addLayer('bitmap');
    store.moveLayer(b.id, -1);
    expect(store.project.layers.map((l) => l.id)).toEqual([b.id, a.id]);
    store.moveLayer(b.id, -1); // already at index 0 — no-op
    expect(store.project.layers.map((l) => l.id)).toEqual([b.id, a.id]);
  });

  it('addLayers with a groupName tags every pushed layer with the same fresh groupId and records one LayerGroup', () => {
    const store = new ProjectStore();
    store.loadProject(blankProject());
    const a = store.addLayer('vector');
    const inserted = store.addLayers([
      { ...a, id: 'l2', name: 'L2' },
      { ...a, id: 'l3', name: 'L3' },
    ], 'Lyrics');
    expect(inserted.every((l) => l.groupId === inserted[0].groupId)).toBe(true);
    expect(store.project.layerGroups).toHaveLength(1);
    expect(store.project.layerGroups![0].name).toBe('Lyrics');
    expect(store.project.layerGroups![0].id).toBe(inserted[0].groupId);
  });

  it('addLayers with no groupName inserts ungrouped layers and adds no LayerGroup', () => {
    const store = new ProjectStore();
    store.loadProject(blankProject());
    const a = store.addLayer('vector');
    const inserted = store.addLayers([{ ...a, id: 'l2', name: 'L2' }]);
    expect(inserted[0].groupId).toBeUndefined();
    expect(store.project.layerGroups ?? []).toHaveLength(0);
  });

  it('toggleLayerGroupCollapsed flips the persisted collapsed flag on that group only', () => {
    const store = new ProjectStore();
    store.loadProject(blankProject());
    const a = store.addLayer('vector');
    store.addLayers([{ ...a, id: 'l2', name: 'L2' }], 'Lyrics');
    const groupId = store.project.layerGroups![0].id;
    store.toggleLayerGroupCollapsed(groupId);
    expect(store.project.layerGroups![0].collapsed).toBe(true);
    store.toggleLayerGroupCollapsed(groupId);
    expect(store.project.layerGroups![0].collapsed).toBe(false);
  });
});

describe('ProjectStore — keyframing', () => {
  it('toggleKeyframing seeds one keyframe at t:0 holding the static value', () => {
    const store = new ProjectStore();
    store.loadProject(blankProject());
    const layer = store.addLayer('vector');
    store.toggleKeyframing(layer.id, 'opacity');
    expect(layer.tracks.opacity).toEqual([{ id: expect.any(String), t: 0, v: 1, ease: 'linear' }]);
  });

  it('toggling keyframing off folds the current playhead value back into statics', () => {
    const store = new ProjectStore();
    store.loadProject(blankProject());
    const layer = store.addLayer('vector');
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
    const layer = store.addLayer('vector');
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
    const layer = store.addLayer('vector');
    store.toggleKeyframing(layer.id, 'opacity');
    const id = layer.tracks.opacity[0].id;
    store.selection.selectKeyframe(id);
    store.deleteKeyframe(id);
    expect(layer.tracks.opacity).toHaveLength(0);
    expect(store.selection.keyframeIds.has(id)).toBe(false);
  });
});

describe('ProjectStore — per-shape keyframing', () => {
  function layerWithRectShape(store: ProjectStore) {
    const layer = store.addLayer('vector');
    layer.params.shapes.push({
      id: 'shape-1', kind: 'rect', x: 0, y: 0, w: 10, h: 10, rotation: 0,
      fill: { kind: 'flat', color: '#fff' }, stroke: 'none', strokeWidth: 0,
    });
    return layer;
  }

  it('toggleShapeKeyframing seeds one keyframe at t:0 holding the current animStatics value', () => {
    const store = new ProjectStore();
    store.loadProject(blankProject());
    const layer = layerWithRectShape(store);
    store.toggleShapeKeyframing(layer.id, 'shape-1', 'opacity');
    const shape = layer.params.shapes[0];
    expect(shape.animTracks?.opacity).toEqual([{ id: expect.any(String), t: 0, v: 1, ease: 'linear' }]);
  });

  it('addShapeKeyframeAtPlayhead inserts the currently-evaluated value and keeps the track sorted', () => {
    const store = new ProjectStore();
    store.loadProject(blankProject());
    const layer = layerWithRectShape(store);
    const shape = layer.params.shapes[0];
    shape.animTracks = { rotation: [{ id: 'a', t: 0, v: 0, ease: 'linear' }, { id: 'b', t: 10, v: 10, ease: 'linear' }] };
    store.seekTo(4);
    const id = store.addShapeKeyframeAtPlayhead(layer.id, 'shape-1', 'rotation')!;
    const inserted = shape.animTracks.rotation!.find((k) => k.id === id)!;
    expect(inserted.t).toBe(4);
    expect(inserted.v).toBe(4);
    expect(shape.animTracks.rotation!.map((k) => k.t)).toEqual([0, 4, 10]);
  });

  it('toggling shape keyframing off folds the current playhead value back into animStatics', () => {
    const store = new ProjectStore();
    store.loadProject(blankProject());
    const layer = layerWithRectShape(store);
    const shape = layer.params.shapes[0];
    shape.animTracks = { scale: [{ id: 'a', t: 0, v: 0, ease: 'linear' }, { id: 'b', t: 10, v: 2, ease: 'linear' }] };
    store.seekTo(5);
    store.toggleShapeKeyframing(layer.id, 'shape-1', 'scale');
    expect(shape.animTracks.scale).toEqual([]);
    expect(shape.animStatics?.scale).toBe(1); // midpoint of 0->2 linear at t=5
  });

  it('setKeyframeValue/deleteKeyframe reach a shape keyframe via the shared global keyframeIndex', () => {
    const store = new ProjectStore();
    store.loadProject(blankProject());
    const layer = layerWithRectShape(store);
    store.toggleShapeKeyframing(layer.id, 'shape-1', 'opacity');
    const shape = layer.params.shapes[0];
    const id = shape.animTracks!.opacity![0].id;
    store.setKeyframeValue(id, 0.25);
    expect(shape.animTracks!.opacity![0].v).toBe(0.25);
    store.deleteKeyframe(id);
    expect(shape.animTracks!.opacity).toHaveLength(0);
  });

  it('duplicating a layer gives its shapes and their keyframes fresh ids, not shared ones', () => {
    const store = new ProjectStore();
    store.loadProject(blankProject());
    const layer = layerWithRectShape(store);
    store.toggleShapeKeyframing(layer.id, 'shape-1', 'opacity');
    const originalShapeId = layer.params.shapes[0].id;
    const originalKfId = layer.params.shapes[0].animTracks!.opacity![0].id;
    const clone = store.duplicateLayer(layer.id)!;
    if (clone.type !== 'vector') throw new Error('expected a vector clone');
    expect(clone.params.shapes[0].id).not.toBe(originalShapeId);
    expect(clone.params.shapes[0].animTracks!.opacity![0].id).not.toBe(originalKfId);
    // The original's own keyframe must still resolve to the ORIGINAL shape,
    // not get silently reassigned to the clone by a shared id colliding in
    // the global keyframeIndex.
    expect(store.selection.keyframeIndex.get(originalKfId)?.shape?.id).toBe(originalShapeId);
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

describe('ProjectStore — loop region', () => {
  it('toggleLoop with a section under the playhead loops that section', () => {
    const store = new ProjectStore();
    const p = blankProject();
    p.sections = [
      { id: 'a', label: 'A', start: 0, end: 20, hue: 0, hue2: 0, intensity: 0.5, noLyrics: false },
      { id: 'b', label: 'B', start: 20, end: 60, hue: 0, hue2: 0, intensity: 0.5, noLyrics: false },
    ];
    store.loadProject(p);
    store.playhead = 30;
    store.toggleLoop();
    expect(store.loopEnabled).toBe(true);
    expect(store.loopRegion).toEqual({ start: 20, end: 60 });
    store.toggleLoop();
    expect(store.loopEnabled).toBe(false);
  });

  it('setLoopRegionToSelectedSection sets the region and enables loop', () => {
    const store = new ProjectStore();
    const p = blankProject();
    p.sections = [{ id: 's1', label: 'S1', start: 5, end: 15, hue: 0, hue2: 0, intensity: 0.5, noLyrics: false }];
    store.loadProject(p);
    store.selection.selectSection('s1');
    store.setLoopRegionToSelectedSection();
    expect(store.loopRegion).toEqual({ start: 5, end: 15 });
    expect(store.loopEnabled).toBe(true);
  });

  it('clearLoopRegion turns loop off and drops the region', () => {
    const store = new ProjectStore();
    store.loadProject(blankProject());
    store.toggleLoop();
    store.clearLoopRegion();
    expect(store.loopEnabled).toBe(false);
    expect(store.loopRegion).toBeNull();
  });
});

describe('ProjectStore — timeline zoom', () => {
  it('zoomTimeline steps and clamps to [10, 400]', () => {
    const store = new ProjectStore();
    store.loadProject(blankProject());
    store.timelinePxPerSec = 390;
    store.zoomTimeline(1);
    expect(store.timelinePxPerSec).toBe(400);
    store.timelinePxPerSec = 20;
    store.zoomTimeline(-1);
    expect(store.timelinePxPerSec).toBe(10);
  });
});

describe('ProjectStore — selectAdjacentLayer', () => {
  it('steps through layers in display order (topmost-first, reverse of the array)', () => {
    const store = new ProjectStore();
    const p = blankProject();
    store.loadProject(p);
    const l1 = store.addLayer('vector');
    const l2 = store.addLayer('vector');
    // Array order is [l1, l2]; display order is reversed: [l2, l1].
    store.selection.selectLayer(l2.id);
    store.selectAdjacentLayer(1);
    expect(store.selection.layerId).toBe(l1.id);
    store.selectAdjacentLayer(-1);
    expect(store.selection.layerId).toBe(l2.id);
  });

  it('does nothing with no layers', () => {
    const store = new ProjectStore();
    store.loadProject(blankProject());
    store.selectAdjacentLayer(1);
    expect(store.selection.layerId).toBeNull();
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
  // Generic over an encode/decode pair now (CapsuleFile/WorldFile in real
  // use) so both Capsule and World share one implementation — a plain JSON
  // codec is enough to exercise the browser (localStorage) branch here;
  // window.native is undefined under jsdom, same as a real browser build.
  const encode = (d: unknown) => new TextEncoder().encode(JSON.stringify(d));
  const decode = (b: Uint8Array) => JSON.parse(new TextDecoder().decode(b));

  it('round-trips a saved entry through localStorage', async () => {
    const a = new AutosaveManager('test-autosave.bin', 'test:autosave:manager', encode, decode);
    await a.saveNow({ hello: 'world' });
    const saved = await a.readSaved();
    expect(saved).toEqual({ hello: 'world' });
    await a.clear();
    expect(await a.readSaved()).toBeNull();
  });
});

describe('ProjectStore visibility keyframing', () => {
  it('a fresh layer has no visibleTrack, so evalVisible falls back to the static flag', () => {
    const store = new ProjectStore();
    const layer = store.addLayer('vector');
    expect(store.isVisibilityKeyframed(layer.id)).toBe(false);
    expect(layer.visibleTrack).toEqual([]);
  });

  it('toggleVisibilityKeyframing seeds one keyframe at t:0 holding the current static value', () => {
    const store = new ProjectStore();
    const layer = store.addLayer('vector');
    layer.visible = false;
    store.toggleVisibilityKeyframing(layer.id);
    expect(store.isVisibilityKeyframed(layer.id)).toBe(true);
    expect(layer.visibleTrack).toEqual([{ id: layer.visibleTrack![0].id, t: 0, v: false }]);
  });

  it('addVisibilityToggleAtPlayhead seeds at t:0 on first use, then adds toggles at the playhead, kept sorted', () => {
    const store = new ProjectStore();
    const layer = store.addLayer('vector');
    layer.visible = true;
    store.addVisibilityToggleAtPlayhead(layer.id); // not yet keyframed: seeds t:0 with the OPPOSITE of the static value
    expect(layer.visibleTrack).toEqual([{ id: layer.visibleTrack![0].id, t: 0, v: false }]);

    store.playhead = 5;
    store.addVisibilityToggleAtPlayhead(layer.id); // already keyframed: adds a toggle AT the playhead instead of re-seeding
    expect(layer.visibleTrack!.map((k) => ({ t: k.t, v: k.v }))).toEqual([
      { t: 0, v: false },
      { t: 5, v: true },
    ]);
  });

  it('disabling visibility keyframing folds the value AT THE PLAYHEAD back into the static flag', () => {
    const store = new ProjectStore();
    const layer = store.addLayer('vector');
    layer.visible = true;
    store.toggleVisibilityKeyframing(layer.id); // seeds [{t:0, v:true}]
    store.playhead = 5;
    layer.visibleTrack!.push({ id: 'kf2', t: 3, v: false });
    store.toggleVisibilityKeyframing(layer.id); // disable — should fold in the value at t=5 (last kf at/before 5 is t=3 -> false)
    expect(layer.visible).toBe(false);
    expect(layer.visibleTrack).toEqual([]);
  });

  it('duplicateLayer gives the clone fresh visibility-keyframe ids, not shared ones', () => {
    const store = new ProjectStore();
    const layer = store.addLayer('vector');
    store.toggleVisibilityKeyframing(layer.id);
    const clone = store.duplicateLayer(layer.id);
    expect(clone!.visibleTrack![0].id).not.toBe(layer.visibleTrack![0].id);
    expect(clone!.visibleTrack![0].v).toBe(layer.visibleTrack![0].v);
  });
});
