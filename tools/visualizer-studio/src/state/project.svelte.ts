import type { AnimKey, AnyLayer, Ease, LayerType, Project, ProjectMeta, Section } from '../types/project';
import { evalTrack } from '../core/ease.js';
import { newLayer, newKeyframeId, newProject as createNewProject } from './factories';
import { HistoryStack } from './history.svelte';
import { SelectionState } from './selection.svelte';

const HUE_KEYS: ReadonlySet<AnimKey> = new Set(['hueA', 'hueB']);

export class ProjectStore {
  project: Project = $state(createNewProject());
  playhead = $state(0);
  playing = $state(false);

  history = new HistoryStack();
  selection = new SelectionState(() => this.project);

  // Explicit commit point for continuous UI-driven edits (keyframe drag, numeric
  // field entry) that mutate `project` directly and only want ONE history entry
  // at the end, instead of one per intermediate frame.
  commit() {
    this.history.commit(this.project);
  }

  loadProject(project: Project) {
    this.project = project;
    this.playhead = 0;
    this.playing = false;
    this.selection.clearAll();
    this.history.reset(project);
  }

  newProject() {
    this.loadProject(createNewProject());
  }

  undo() {
    const prev = this.history.undo(this.project);
    if (prev) this.project = prev;
  }

  redo() {
    const next = this.history.redo(this.project);
    if (next) this.project = next;
  }

  // ---- playback ----
  seekTo(t: number) {
    this.playhead = Math.max(0, Math.min(t, this.project.meta.songEnd));
  }

  togglePlay() {
    this.playing = !this.playing;
  }

  stop() {
    this.playing = false;
    this.playhead = 0;
  }

  // ---- meta ----
  updateMeta(patch: Partial<ProjectMeta>) {
    Object.assign(this.project.meta, patch);
    this.commit();
  }

  // ---- layers ----
  addLayer(type: LayerType): AnyLayer {
    const layer = newLayer(type);
    this.project.layers.push(layer);
    // Re-fetch from the reactive array: `layer` is the pre-$state object, a
    // separate identity from the proxy Svelte wraps it in once it's inside
    // `this.project` — callers need the live reference, not the stale one.
    const reactive = this.project.layers[this.project.layers.length - 1];
    this.selection.selectLayer(reactive.id);
    this.commit();
    return reactive;
  }

  deleteLayer(id: string) {
    const idx = this.project.layers.findIndex((l) => l.id === id);
    if (idx < 0) return;
    this.project.layers.splice(idx, 1);
    if (this.selection.layerId === id) this.selection.selectLayer(null);
    this.commit();
  }

  duplicateLayer(id: string): AnyLayer | null {
    const srcIdx = this.project.layers.findIndex((l) => l.id === id);
    if (srcIdx < 0) return null;
    const clone = structuredClone($state.snapshot(this.project.layers[srcIdx])) as AnyLayer;
    clone.id = `layer_${crypto.randomUUID()}`;
    clone.name = `${clone.name} copy`;
    for (const key of Object.keys(clone.tracks) as AnimKey[]) {
      clone.tracks[key] = clone.tracks[key].map((kf) => ({ ...kf, id: newKeyframeId() }));
    }
    this.project.layers.splice(srcIdx + 1, 0, clone);
    const reactive = this.project.layers[srcIdx + 1];
    this.selection.selectLayer(reactive.id);
    this.commit();
    return reactive;
  }

  moveLayer(id: string, dir: -1 | 1) {
    const layers = this.project.layers;
    const idx = layers.findIndex((l) => l.id === id);
    const swapWith = idx + dir;
    if (idx < 0 || swapWith < 0 || swapWith >= layers.length) return;
    [layers[idx], layers[swapWith]] = [layers[swapWith], layers[idx]];
    this.commit();
  }

  toggleLayerVisibility(id: string) {
    const layer = this.project.layers.find((l) => l.id === id);
    if (!layer) return;
    layer.visible = !layer.visible;
    this.commit();
  }

  renameLayer(id: string, name: string) {
    const layer = this.project.layers.find((l) => l.id === id);
    if (!layer) return;
    layer.name = name;
    this.commit();
  }

  // ---- keyframing ----
  // Enabling keyframing on a static property seeds it with one keyframe at t:0
  // holding the current static value. Disabling it folds the value AT THE CURRENT
  // PLAYHEAD back into the static so turning keyframing off doesn't jump the layer.
  toggleKeyframing(layerId: string, key: AnimKey) {
    const layer = this.project.layers.find((l) => l.id === layerId);
    if (!layer) return;
    if (layer.tracks[key].length) {
      layer.statics[key] = evalTrack(layer.tracks[key], this.playhead, layer.statics[key], HUE_KEYS.has(key));
      layer.tracks[key] = [];
    } else {
      layer.tracks[key] = [{ id: newKeyframeId(), t: 0, v: layer.statics[key], ease: 'linear' }];
    }
    this.commit();
  }

  addKeyframeAtPlayhead(layerId: string, key: AnimKey): string | null {
    const layer = this.project.layers.find((l) => l.id === layerId);
    if (!layer) return null;
    if (!layer.tracks[key].length) {
      this.toggleKeyframing(layerId, key);
      return layer.tracks[key][0]?.id ?? null;
    }
    const value = evalTrack(layer.tracks[key], this.playhead, layer.statics[key], HUE_KEYS.has(key));
    const id = newKeyframeId();
    layer.tracks[key].push({ id, t: this.playhead, v: value, ease: 'linear' });
    layer.tracks[key].sort((a, b) => a.t - b.t);
    this.commit();
    return id;
  }

  deleteKeyframe(id: string) {
    const ref = this.selection.keyframeIndex.get(id);
    if (!ref) return;
    ref.layer.tracks[ref.trackKey].splice(ref.index, 1);
    if (this.selection.keyframeIds.has(id)) {
      const next = new Set(this.selection.keyframeIds);
      next.delete(id);
      this.selection.keyframeIds = next;
    }
    this.commit();
  }

  setKeyframeEase(id: string, ease: Ease) {
    const ref = this.selection.keyframeIndex.get(id);
    if (!ref) return;
    ref.layer.tracks[ref.trackKey][ref.index].ease = ease;
    this.commit();
  }

  // Continuous drag — caller commits once on pointerup via `commit()`.
  moveKeyframeTime(id: string, t: number) {
    const ref = this.selection.keyframeIndex.get(id);
    if (!ref) return;
    ref.layer.tracks[ref.trackKey][ref.index].t = Math.max(0, t);
    ref.layer.tracks[ref.trackKey].sort((a, b) => a.t - b.t);
  }

  // ---- sections ----
  addSection(): Section {
    const sections = this.project.sections;
    const last = sections[sections.length - 1];
    const start = last ? last.end : 0;
    const section = {
      id: `sec_${crypto.randomUUID()}`,
      label: `Section ${sections.length + 1}`,
      start,
      end: start + 10,
      hue: Math.round(Math.random() * 360),
      hue2: Math.round(Math.random() * 360),
      intensity: 0.5,
      noLyrics: false,
    };
    sections.push(section);
    const reactive = sections[sections.length - 1];
    this.commit();
    return reactive;
  }

  deleteSection(id: string) {
    if (this.project.sections.length <= 1) return;
    this.project.sections = this.project.sections.filter((s) => s.id !== id);
    if (this.selection.sectionId === id) this.selection.selectSection(null);
    this.commit();
  }

  // QoL: self-correcting clamp instead of silently accepting an inverted block —
  // matches the popover's existing "auto-apply on input, no separate Save" UX.
  updateSection(id: string, patch: Partial<Section>) {
    const sec = this.project.sections.find((s) => s.id === id);
    if (!sec) return;
    Object.assign(sec, patch);
    if (sec.end < sec.start + 0.1) sec.end = sec.start + 0.1;
    this.commit();
  }
}
