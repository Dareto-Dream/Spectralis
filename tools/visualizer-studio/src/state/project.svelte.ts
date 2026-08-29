import type { AnimKey, AnyLayer, Ease, Layer, LayerType, Project, ProjectMeta, Section } from '../types/project';
import { evalTrack } from '../core/ease.js';
import { sectionAt, evalVisible } from '../core/render.js';
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

  // Ephemeral, session-only (not persisted, not undoable) — matches how solo
  // works in most DAWs/AE: a temporary isolation toggle, not an authored property.
  soloedLayerIds: Set<string> = $state(new Set());

  // Loop region (plan QoL §J) — also ephemeral: a playback convenience, not
  // authored project data. `null` region = loop the whole song when enabled.
  loopEnabled = $state(false);
  loopRegion: { start: number; end: number } | null = $state(null);

  // Timeline zoom — lifted out of TimelineCanvas.svelte's local state (was a
  // plain `let`) so the global `+`/`-` shortcuts in lib/keymap.ts can reach it
  // without the keymap needing a reference to a specific panel instance.
  timelinePxPerSec = $state(80);
  zoomTimeline(dir: -1 | 1) {
    this.timelinePxPerSec = Math.max(10, Math.min(400, this.timelinePxPerSec + dir * 20));
  }

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

  // Toggling loop on with nothing selected loops the section under the
  // playhead right now — reuses `sectionAt`, already the source of truth for
  // "what section is this" everywhere else in the app.
  toggleLoop() {
    this.loopEnabled = !this.loopEnabled;
    if (this.loopEnabled && !this.loopRegion) {
      const sec = sectionAt(this.project.sections, this.playhead);
      this.loopRegion = sec ? { start: sec.start, end: sec.end } : { start: 0, end: this.project.meta.songEnd };
    }
  }

  setLoopRegionToSelectedSection() {
    const sec = this.project.sections.find((s) => s.id === this.selection.sectionId);
    if (!sec) return;
    this.loopRegion = { start: sec.start, end: sec.end };
    this.loopEnabled = true;
  }

  clearLoopRegion() {
    this.loopEnabled = false;
    this.loopRegion = null;
  }

  // ---- meta ----
  updateMeta(patch: Partial<ProjectMeta>) {
    Object.assign(this.project.meta, patch);
    this.commit();
  }

  // ---- layers ----
  addLayer<T extends LayerType>(type: T): Layer<T> {
    const layer = newLayer(type);
    this.project.layers.push(layer as AnyLayer);
    // Re-fetch from the reactive array: `layer` is the pre-$state object, a
    // separate identity from the proxy Svelte wraps it in once it's inside
    // `this.project` — callers need the live reference, not the stale one.
    const reactive = this.project.layers[this.project.layers.length - 1] as Layer<T>;
    this.selection.selectLayer(reactive.id);
    this.commit();
    return reactive;
  }

  // Non-destructive multi-layer insert — backs both the Assets > Templates
  // "layer templates" (lib/vectorize.ts-built Orb/Wheel/etc., inserted into
  // the current project rather than replacing it like the whole-project
  // TEMPLATES flow does) and the Lyrics Importer's generated group. All
  // pushed layers share one new LayerGroup when `groupName` is given, one
  // history commit for the whole batch.
  addLayers(layers: AnyLayer[], groupName?: string): AnyLayer[] {
    if (!layers.length) return [];
    const groupId = groupName ? `group_${crypto.randomUUID()}` : undefined;
    if (groupId) {
      for (const l of layers) l.groupId = groupId;
      this.project.layerGroups = [...(this.project.layerGroups ?? []), { id: groupId, name: groupName! }];
    }
    const startLen = this.project.layers.length;
    this.project.layers.push(...layers);
    // Re-fetch from the reactive array — see addLayer()'s comment on why the
    // pre-$state objects aren't the identity callers need.
    const reactive = this.project.layers.slice(startLen);
    this.selection.selectLayer(reactive[reactive.length - 1]?.id ?? null);
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
    if (clone.visibleTrack) clone.visibleTrack = clone.visibleTrack.map((kf) => ({ ...kf, id: newKeyframeId() }));
    this.project.layers.splice(srcIdx + 1, 0, clone);
    const reactive = this.project.layers[srcIdx + 1];
    this.selection.selectLayer(reactive.id);
    this.commit();
    return reactive;
  }

  // Drag-to-reorder counterpart to the ↑/↓ buttons — both end up calling the
  // same underlying array mutation so behavior is identical either way.
  reorderLayer(draggedId: string, ontoId: string) {
    const layers = this.project.layers;
    const from = layers.findIndex((l) => l.id === draggedId);
    const to = layers.findIndex((l) => l.id === ontoId);
    if (from < 0 || to < 0 || from === to) return;
    const [moved] = layers.splice(from, 1);
    layers.splice(to, 0, moved);
    this.commit();
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

  // ---- visibility keyframing (mirrors toggleKeyframing/addKeyframeAt for
  // the numeric ANIM_KEYS tracks, but for the separate boolean visibleTrack) ----
  isVisibilityKeyframed(layerId: string): boolean {
    const layer = this.project.layers.find((l) => l.id === layerId);
    return !!layer?.visibleTrack?.length;
  }

  // Enabling seeds one keyframe at t:0 holding the current static value —
  // same convention as toggleKeyframing. Disabling folds the value AT THE
  // CURRENT PLAYHEAD back into the static flag so turning it off doesn't
  // change what's on screen right now.
  toggleVisibilityKeyframing(layerId: string) {
    const layer = this.project.layers.find((l) => l.id === layerId);
    if (!layer) return;
    if (layer.visibleTrack?.length) {
      layer.visible = evalVisible(layer, this.playhead);
      layer.visibleTrack = [];
    } else {
      layer.visibleTrack = [{ id: newKeyframeId(), t: 0, v: layer.visible }];
    }
    this.commit();
  }

  // Backs the Inspector's "+Key (toggle) at playhead" button — adds a
  // keyframe that flips whatever the evaluated visibility is right now.
  // Seeds at t:0 if this is the first keyframe (mirrors toggleKeyframing's
  // seed convention), otherwise adds at the playhead like a normal track.
  addVisibilityToggleAtPlayhead(layerId: string) {
    const layer = this.project.layers.find((l) => l.id === layerId);
    if (!layer) return;
    const track = layer.visibleTrack ?? [];
    const current = track.length ? evalVisible(layer, this.playhead) : layer.visible;
    const next = { id: newKeyframeId(), t: track.length ? this.playhead : 0, v: !current };
    layer.visibleTrack = [...track, next].sort((a, b) => a.t - b.t);
    this.commit();
  }

  deleteVisibilityKeyframe(layerId: string, keyframeId: string) {
    const layer = this.project.layers.find((l) => l.id === layerId);
    if (!layer?.visibleTrack) return;
    layer.visibleTrack = layer.visibleTrack.filter((kf) => kf.id !== keyframeId);
    this.commit();
  }

  renameLayer(id: string, name: string) {
    const layer = this.project.layers.find((l) => l.id === id);
    if (!layer) return;
    layer.name = name;
    this.commit();
  }

  toggleLayerLock(id: string) {
    const layer = this.project.layers.find((l) => l.id === id);
    if (!layer) return;
    layer.locked = !layer.locked;
    this.commit();
  }

  // ↑/↓ layer-list navigation (plan QoL §B) steps through the layer list in
  // DISPLAY order (topmost-first — the reverse of the underlying array, same
  // flip LayersPanel.svelte already applies for its own rendering) rather
  // than the raw array order, so "down" always means "down the visible list".
  selectAdjacentLayer(dir: -1 | 1) {
    const display = [...this.project.layers].reverse();
    if (!display.length) return;
    const curIdx = display.findIndex((l) => l.id === this.selection.layerId);
    const nextIdx = curIdx < 0 ? 0 : Math.max(0, Math.min(display.length - 1, curIdx + dir));
    this.selection.selectLayer(display[nextIdx].id);
  }

  toggleLayerSolo(id: string) {
    const next = new Set(this.soloedLayerIds);
    if (next.has(id)) next.delete(id);
    else next.add(id);
    this.soloedLayerIds = next;
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
    return this.addKeyframeAt(layerId, key, this.playhead);
  }

  // Backs both the Inspector's "+Key at playhead" button and the timeline's
  // double-click-to-add-keyframe fast path.
  addKeyframeAt(layerId: string, key: AnimKey, t: number): string | null {
    const layer = this.project.layers.find((l) => l.id === layerId);
    if (!layer) return null;
    if (!layer.tracks[key].length) {
      this.toggleKeyframing(layerId, key);
      return layer.tracks[key][0]?.id ?? null;
    }
    const value = evalTrack(layer.tracks[key], t, layer.statics[key], HUE_KEYS.has(key));
    const id = newKeyframeId();
    layer.tracks[key].push({ id, t, v: value, ease: 'linear' });
    layer.tracks[key].sort((a, b) => a.t - b.t);
    this.commit();
    return id;
  }

  // Creates fresh keyframes (new ids) from clipboard entries — Ctrl+V / Ctrl+Shift+V.
  pasteKeyframes(entries: { layerId: string; trackKey: AnimKey; t: number; v: number; ease: Ease }[]) {
    if (!entries.length) return;
    for (const e of entries) {
      const layer = this.project.layers.find((l) => l.id === e.layerId);
      if (!layer) continue;
      layer.tracks[e.trackKey].push({ id: newKeyframeId(), t: Math.max(0, e.t), v: e.v, ease: e.ease });
      layer.tracks[e.trackKey].sort((a, b) => a.t - b.t);
    }
    this.commit();
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

  // Continuous drag counterpart to moveKeyframeTime, for the same keyframe's
  // VALUE instead of its time — backs the Workspace canvas's transform tool
  // dragging a property that's already keyframed (PropRow's "+Key" button is
  // the same idea: seed/find the keyframe at the playhead, then this updates
  // it live). Caller commits once on pointerup via `commit()`.
  setKeyframeValue(id: string, v: number) {
    const ref = this.selection.keyframeIndex.get(id);
    if (!ref) return;
    ref.layer.tracks[ref.trackKey][ref.index].v = v;
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
    this.updateSectionLive(id, patch);
    this.commit();
  }

  // Continuous edge-drag — caller commits once on pointerup via `commit()`.
  updateSectionLive(id: string, patch: Partial<Section>) {
    const sec = this.project.sections.find((s) => s.id === id);
    if (!sec) return;
    Object.assign(sec, patch);
    if (sec.end < sec.start + 0.1) sec.end = sec.start + 0.1;
  }
}
