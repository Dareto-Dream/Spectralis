<script lang="ts">
  // The actual editable canvas — drawing/point manipulation happens here, not
  // in the read-only Preview viewport (PreviewCanvas.svelte, which stays
  // pointer-handler-free). Renders the same live frame drawPreviewFrame
  // produces, then overlays selection/transform handles and pen-tool anchors
  // on top, and wires pointer events to whichever tool is active in
  // state/toolState.svelte.ts.
  import { onMount, onDestroy } from 'svelte';
  import type { ProjectStore } from '../state/project.svelte';
  import type { AudioState } from '../state/audio.svelte';
  import { drawPreviewFrame, initialFrameState, aspectSize } from './drawPreview';
  import { evalTrack } from '../core/ease.js';
  import { toolState } from '../state/toolState.svelte';
  import { workspaceView } from '../state/workspaceView.svelte';
  import {
    resolveLayerTransform,
    screenToLayerLocal,
    layerLocalToScreen,
    layerLocalBounds,
    shapeLocalBounds,
    hitTestShapeLocal,
    screenDist,
    newAnchor,
    type LayerTransform,
  } from '../lib/vectorHitTest';
  import type { AnimKey, AnyLayer, VectorShape, VectorPathShape } from '../types/project';
  import { toast } from '../state/toast.svelte';
  import { assetDrop } from '../lib/dragAsset';
  import { addImageLayerFromAsset } from '../lib/imageLayer';
  import { isTextInput } from '../lib/keymap';
  import ZoomIn from '@lucide/svelte/icons/zoom-in';
  import ZoomOut from '@lucide/svelte/icons/zoom-out';
  import RotateCcw from '@lucide/svelte/icons/rotate-ccw';

  let { store, audio }: { store: ProjectStore; audio: AudioState } = $props();

  let wrapEl: HTMLDivElement;
  let canvas: HTMLCanvasElement;
  let ctx: CanvasRenderingContext2D;
  let raf = 0;
  const frameState = initialFrameState();
  const HANDLE_HIT_PX = 10;

  $effect(() => {
    const { w, h } = aspectSize(store.project.meta.aspect);
    if (canvas.width !== w) canvas.width = w;
    if (canvas.height !== h) canvas.height = h;
  });

  function selectedLayer(): AnyLayer | undefined {
    return store.project.layers.find((l) => l.id === store.selection.layerId);
  }

  // ---- overlay drawing ----
  function drawSelectionOverlay() {
    const layer = selectedLayer();
    if (!layer) return;
    const tr = resolveLayerTransform(layer, store.playhead, canvas.width, canvas.height);
    const b = layerLocalBounds(layer, canvas.width, canvas.height);
    const corners = [
      layerLocalToScreen(b.x, b.y, tr),
      layerLocalToScreen(b.x + b.w, b.y, tr),
      layerLocalToScreen(b.x + b.w, b.y + b.h, tr),
      layerLocalToScreen(b.x, b.y + b.h, tr),
    ];
    ctx.save();
    ctx.strokeStyle = '#5ec8ff';
    ctx.lineWidth = 1;
    ctx.setLineDash([4, 3]);
    ctx.beginPath();
    ctx.moveTo(corners[0].x, corners[0].y);
    for (const c of corners.slice(1)) ctx.lineTo(c.x, c.y);
    ctx.closePath();
    ctx.stroke();
    ctx.setLineDash([]);
    if (!layer.locked && toolState.active === 'select') {
      ctx.fillStyle = '#5ec8ff';
      for (const c of corners) {
        ctx.fillRect(c.x - 3, c.y - 3, 6, 6);
      }
      const rotateHandle = layerLocalToScreen((b.x + b.x + b.w) / 2, b.y - 24, tr);
      const topMid = layerLocalToScreen((b.x + b.x + b.w) / 2, b.y, tr);
      ctx.beginPath();
      ctx.moveTo(topMid.x, topMid.y);
      ctx.lineTo(rotateHandle.x, rotateHandle.y);
      ctx.stroke();
      ctx.beginPath();
      ctx.arc(rotateHandle.x, rotateHandle.y, 4, 0, Math.PI * 2);
      ctx.fill();
    }
    ctx.restore();
  }

  // The Select tool's real click target — the single shape under the cursor
  // (see pickShapeAt), not the layer's whole bounding box. Drawn distinctly
  // from the layer outline above so it's clear which of the two you're about
  // to drag: the layer outline's corner/rotate handles transform the WHOLE
  // layer, dragging inside this box moves just this one shape.
  function drawShapeSelectionOverlay() {
    if (toolState.active !== 'select' || !toolState.selectedShapeId) return;
    const layer = selectedLayer();
    if (!layer || layer.type !== 'vector') return;
    const shape = layer.params.shapes.find((s) => s.id === toolState.selectedShapeId);
    if (!shape) return;
    const tr = resolveLayerTransform(layer, store.playhead, canvas.width, canvas.height);
    const b = shapeLocalBounds(shape);
    const corners = [
      layerLocalToScreen(b.x, b.y, tr),
      layerLocalToScreen(b.x + b.w, b.y, tr),
      layerLocalToScreen(b.x + b.w, b.y + b.h, tr),
      layerLocalToScreen(b.x, b.y + b.h, tr),
    ];
    ctx.save();
    ctx.strokeStyle = '#ff9d5c';
    ctx.lineWidth = 1.5;
    ctx.setLineDash([3, 3]);
    ctx.beginPath();
    ctx.moveTo(corners[0].x, corners[0].y);
    for (const c of corners.slice(1)) ctx.lineTo(c.x, c.y);
    ctx.closePath();
    ctx.stroke();
    ctx.restore();
  }

  function drawPenOverlay() {
    if ((toolState.active !== 'pen' && toolState.active !== 'node') || !toolState.editingShapeId) return;
    const layer = selectedLayer();
    if (!layer || layer.type !== 'vector') return;
    const shape = layer.params.shapes.find((s) => s.id === toolState.editingShapeId);
    if (!shape || shape.kind !== 'path') return;
    const tr = resolveLayerTransform(layer, store.playhead, canvas.width, canvas.height);
    ctx.save();
    ctx.strokeStyle = '#ffcf5c';
    ctx.fillStyle = '#ffcf5c';
    for (const anchor of shape.points) {
      const p = layerLocalToScreen(anchor.x, anchor.y, tr);
      if (anchor.handleOut) {
        const h = layerLocalToScreen(anchor.x + anchor.handleOut.x, anchor.y + anchor.handleOut.y, tr);
        ctx.beginPath();
        ctx.moveTo(p.x, p.y);
        ctx.lineTo(h.x, h.y);
        ctx.stroke();
        ctx.beginPath();
        ctx.arc(h.x, h.y, 3, 0, Math.PI * 2);
        ctx.fill();
      }
      if (anchor.handleIn) {
        const h = layerLocalToScreen(anchor.x + anchor.handleIn.x, anchor.y + anchor.handleIn.y, tr);
        ctx.beginPath();
        ctx.moveTo(p.x, p.y);
        ctx.lineTo(h.x, h.y);
        ctx.stroke();
        ctx.beginPath();
        ctx.arc(h.x, h.y, 3, 0, Math.PI * 2);
        ctx.fill();
      }
      ctx.strokeRect(p.x - 3, p.y - 3, 6, 6);
    }
    ctx.restore();
  }

  // While actively painting, the layer's own committed dataUrl is stale (it
  // only gets written back on pointerup — see onBrushPointerUp) — that used
  // to mean strokes only appeared once you let go. Instead, skip that one
  // layer in the normal pass and draw the live in-progress paint buffer on
  // top ourselves, at the exact same transform/opacity renderLayerAt would
  // use, so every frame shows the stroke as it's actually being painted.
  function drawLiveBrushLayer() {
    if (!brushOffscreen || !brushLayerId) return;
    const layer = store.project.layers.find((l) => l.id === brushLayerId);
    if (!layer || layer.type !== 'bitmap') return;
    const tr = resolveLayerTransform(layer, store.playhead, canvas.width, canvas.height);
    const opacity = evalTrack(layer.tracks.opacity, store.playhead, layer.statics.opacity);
    if (opacity <= 0.004) return;
    ctx.save();
    ctx.globalAlpha = opacity;
    ctx.translate(tr.x, tr.y);
    ctx.rotate(tr.rotation);
    ctx.scale(tr.worldScale, tr.worldScale);
    ctx.drawImage(brushOffscreen, -canvas.width / 2, -canvas.height / 2, canvas.width, canvas.height);
    ctx.restore();
  }

  function tick(now: number) {
    raf = requestAnimationFrame(tick);
    const level = audio.loaded ? audio.currentLevel() : { peak: 0, rms: 0 };
    const paintingLayerId = brushOffscreen ? brushLayerId : null;
    drawPreviewFrame(ctx, canvas.width, canvas.height, store.project, store.playhead, now, level, frameState, store.soloedLayerIds, {
      transparentBg: true,
      skipLayerId: paintingLayerId,
    });
    if (paintingLayerId) drawLiveBrushLayer();
    drawSelectionOverlay();
    drawShapeSelectionOverlay();
    drawPenOverlay();
  }

  onMount(() => {
    ctx = canvas.getContext('2d')!;
    raf = requestAnimationFrame(tick);
  });
  onDestroy(() => cancelAnimationFrame(raf));

  // ---- view transform (pan/zoom/rotate of the CANVAS ELEMENT itself, via
  // CSS transform: translate(pan) rotate(rotation) scale(zoom) with
  // transform-origin: center) <-> screen-pixel math. Kept in exact sync with
  // the CSS in the template below — see workspaceView.svelte.ts's doc for why
  // this is view state, not project state. ----
  function wrapCenter(): { x: number; y: number } {
    const r = wrapEl.getBoundingClientRect();
    return { x: r.left + r.width / 2, y: r.top + r.height / 2 };
  }

  function toCanvasPoint(e: { clientX: number; clientY: number }): { x: number; y: number } {
    const c = wrapCenter();
    const dx = e.clientX - c.x - workspaceView.panX;
    const dy = e.clientY - c.y - workspaceView.panY;
    const cos = Math.cos(-workspaceView.rotation);
    const sin = Math.sin(-workspaceView.rotation);
    const rx = (dx * cos - dy * sin) / workspaceView.zoom;
    const ry = (dx * sin + dy * cos) / workspaceView.zoom;
    return { x: rx + canvas.width / 2, y: ry + canvas.height / 2 };
  }

  // ---- pan (Space+drag or middle-mouse-drag) / rotate (Alt+Shift+drag) ----
  let spaceHeld = $state(false);
  let panDragging = $state(false);
  let rotateDragging = $state(false);
  let panStartClient = { x: 0, y: 0 };
  let panStartView = { x: 0, y: 0 };
  let rotateStartAngle = 0;
  let rotateStartView = 0;

  function onWrapWheel(e: WheelEvent) {
    e.preventDefault();
    if (e.ctrlKey || e.metaKey) {
      // Zoom, pinned to the point currently under the cursor.
      const before = toCanvasPoint(e);
      const dir = e.deltaY < 0 ? 1 : -1;
      const newZoom = Math.min(8, Math.max(0.1, workspaceView.zoom * (dir > 0 ? 1.1 : 1 / 1.1)));
      const c = wrapCenter();
      const relX = (before.x - canvas.width / 2) * newZoom;
      const relY = (before.y - canvas.height / 2) * newZoom;
      const cos = Math.cos(workspaceView.rotation);
      const sin = Math.sin(workspaceView.rotation);
      workspaceView.panX = e.clientX - c.x - (relX * cos - relY * sin);
      workspaceView.panY = e.clientY - c.y - (relX * sin + relY * cos);
      workspaceView.zoom = newZoom;
    } else {
      // Plain scroll pans, matching the standard "scroll the canvas" feel.
      workspaceView.panX -= e.deltaX;
      workspaceView.panY -= e.deltaY;
    }
  }

  function onWrapPointerDown(e: PointerEvent) {
    if (e.button === 1 || (e.button === 0 && spaceHeld)) {
      panDragging = true;
      panStartClient = { x: e.clientX, y: e.clientY };
      panStartView = { x: workspaceView.panX, y: workspaceView.panY };
      (e.currentTarget as HTMLElement).setPointerCapture(e.pointerId);
      e.preventDefault();
      return;
    }
    if (e.button === 0 && e.altKey && e.shiftKey) {
      rotateDragging = true;
      const c = wrapCenter();
      rotateStartAngle = Math.atan2(e.clientY - c.y, e.clientX - c.x);
      rotateStartView = workspaceView.rotation;
      (e.currentTarget as HTMLElement).setPointerCapture(e.pointerId);
      e.preventDefault();
      return;
    }
    onPointerDown(e);
  }

  function onWrapPointerMove(e: PointerEvent) {
    if (panDragging) {
      workspaceView.panX = panStartView.x + (e.clientX - panStartClient.x);
      workspaceView.panY = panStartView.y + (e.clientY - panStartClient.y);
      return;
    }
    if (rotateDragging) {
      const c = wrapCenter();
      const a = Math.atan2(e.clientY - c.y, e.clientX - c.x);
      workspaceView.rotation = rotateStartView + (a - rotateStartAngle);
      return;
    }
    onPointerMove(e);
  }

  function onWrapPointerUp(e: PointerEvent) {
    if (panDragging || rotateDragging) {
      panDragging = false;
      rotateDragging = false;
      return;
    }
    onPointerUp();
  }

  function onWindowKeyup(e: KeyboardEvent) {
    if (e.code === 'Space') spaceHeld = false;
  }

  // ---- select/transform drag state ----
  type DragMode = 'move' | 'scale' | 'rotate' | null;
  let dragMode: DragMode = null;
  let dragLayerId: string | null = null;
  let dragTr: LayerTransform | null = null;
  let dragStartCanvas = { x: 0, y: 0 };
  let dragStartLocal = { x: 0, y: 0 };
  let kfIds: Partial<Record<AnimKey, string | null>> = {};

  function ensureKeyframeId(layer: AnyLayer, key: AnimKey): string | null {
    const track = layer.tracks[key];
    if (!track.length) return null; // pure static write
    const existing = track.find((kf) => Math.abs(kf.t - store.playhead) < 0.001);
    return existing ? existing.id : store.addKeyframeAtPlayhead(layer.id, key);
  }

  function writeValue(layer: AnyLayer, key: AnimKey, kfId: string | null, v: number) {
    if (kfId) store.setKeyframeValue(kfId, v);
    else layer.statics[key] = v;
  }

  function hitTestHandles(layer: AnyLayer, tr: LayerTransform, pt: { x: number; y: number }): DragMode {
    if (layer.locked) return null;
    const b = layerLocalBounds(layer, canvas.width, canvas.height);
    const rotateHandle = layerLocalToScreen((b.x + b.x + b.w) / 2, b.y - 24, tr);
    if (screenDist(pt.x, pt.y, rotateHandle.x, rotateHandle.y) < HANDLE_HIT_PX) return 'rotate';
    const corners = [
      layerLocalToScreen(b.x, b.y, tr),
      layerLocalToScreen(b.x + b.w, b.y, tr),
      layerLocalToScreen(b.x + b.w, b.y + b.h, tr),
      layerLocalToScreen(b.x, b.y + b.h, tr),
    ];
    for (const c of corners) {
      if (screenDist(pt.x, pt.y, c.x, c.y) < HANDLE_HIT_PX) return 'scale';
    }
    return null;
  }

  // Bitmap-only fallback (see onSelectPointerDown) — bitmaps have no
  // sub-shapes to target individually, so the whole layer's full-canvas
  // bounds is the only thing there is to click.
  function pickLayerAt(pt: { x: number; y: number }): AnyLayer | undefined {
    for (let i = store.project.layers.length - 1; i >= 0; i--) {
      const layer = store.project.layers[i];
      if (layer.locked) continue;
      const tr = resolveLayerTransform(layer, store.playhead, canvas.width, canvas.height);
      const local = screenToLayerLocal(pt.x, pt.y, tr);
      const b = layerLocalBounds(layer, canvas.width, canvas.height);
      if (local.x >= b.x && local.x <= b.x + b.w && local.y >= b.y && local.y <= b.y + b.h) return layer;
    }
    return undefined;
  }

  // The real click target for a vector layer — the single TOPMOST shape
  // under the cursor, tested against its own actual geometry (see
  // hitTestShapeLocal), not the layer's overall bounding box. This is what
  // makes "click a shape" select just that shape instead of the whole layer.
  function pickShapeAt(pt: { x: number; y: number }): { layer: AnyLayer; shape: VectorShape } | null {
    for (let i = store.project.layers.length - 1; i >= 0; i--) {
      const layer = store.project.layers[i];
      if (layer.locked || !layer.visible || layer.type !== 'vector') continue;
      const tr = resolveLayerTransform(layer, store.playhead, canvas.width, canvas.height);
      const local = screenToLayerLocal(pt.x, pt.y, tr);
      for (let j = layer.params.shapes.length - 1; j >= 0; j--) {
        const shape = layer.params.shapes[j];
        if (hitTestShapeLocal(shape, local)) return { layer, shape };
      }
    }
    return null;
  }

  // Moves a shape by a LOCAL-space (dx, dy) — applied against a snapshot
  // taken at drag start (not incrementally against the live shape) so
  // repeated small pointermove deltas can't accumulate rounding drift, same
  // reasoning as evalScaleAtDragStart/evalRotationAtDragStart below.
  function applyShapeMove(shape: VectorShape, snapshot: VectorShape, dx: number, dy: number) {
    if (shape.kind === 'line' && snapshot.kind === 'line') {
      shape.x1 = snapshot.x1 + dx;
      shape.y1 = snapshot.y1 + dy;
      shape.x2 = snapshot.x2 + dx;
      shape.y2 = snapshot.y2 + dy;
    } else if (shape.kind === 'path' && snapshot.kind === 'path') {
      shape.points = snapshot.points.map((p) => ({ ...p, x: p.x + dx, y: p.y + dy }));
    } else if ('x' in shape && 'x' in snapshot) {
      shape.x = snapshot.x + dx;
      shape.y = snapshot.y + dy;
    }
  }

  let shapeDrag: { layerId: string; shapeId: string; startLocal: { x: number; y: number }; snapshot: VectorShape } | null = null;

  function onSelectPointerDown(pt: { x: number; y: number }) {
    const layer = selectedLayer();
    if (layer && !layer.locked) {
      // The selected layer's own corner/rotate handles always take priority
      // — an explicit, deliberate way to transform the WHOLE layer, kept
      // available alongside per-shape selection below.
      const tr = resolveLayerTransform(layer, store.playhead, canvas.width, canvas.height);
      const mode = hitTestHandles(layer, tr, pt);
      if (mode) {
        dragMode = mode;
        dragLayerId = layer.id;
        dragTr = tr;
        dragStartCanvas = pt;
        kfIds = { scale: ensureKeyframeId(layer, 'scale'), rotation: ensureKeyframeId(layer, 'rotation') };
        return;
      }
    }
    // Click a shape: select + start moving just THAT shape (never "the whole
    // layer" — see applyShapeMove/pickShapeAt's docs).
    const hitShape = pickShapeAt(pt);
    if (hitShape) {
      store.selection.selectLayer(hitShape.layer.id);
      toolState.selectedShapeId = hitShape.shape.id;
      const tr = resolveLayerTransform(hitShape.layer, store.playhead, canvas.width, canvas.height);
      shapeDrag = {
        layerId: hitShape.layer.id,
        shapeId: hitShape.shape.id,
        startLocal: screenToLayerLocal(pt.x, pt.y, tr),
        snapshot: structuredClone(hitShape.shape),
      };
      return;
    }
    // No shape hit — the only other clickable body is a bitmap layer's (it
    // has no sub-shapes of its own to target).
    const hitLayer = pickLayerAt(pt);
    if (hitLayer && hitLayer.type === 'bitmap' && !hitLayer.locked) {
      const tr = resolveLayerTransform(hitLayer, store.playhead, canvas.width, canvas.height);
      dragMode = 'move';
      dragLayerId = hitLayer.id;
      dragTr = tr;
      dragStartCanvas = pt;
      dragStartLocal = screenToLayerLocal(pt.x, pt.y, tr);
      kfIds = { x: ensureKeyframeId(hitLayer, 'x'), y: ensureKeyframeId(hitLayer, 'y') };
      store.selection.selectLayer(hitLayer.id);
      toolState.selectedShapeId = null;
      return;
    }
    store.selection.selectLayer(null);
    toolState.selectedShapeId = null;
  }

  function onSelectPointerMove(pt: { x: number; y: number }) {
    if (shapeDrag) {
      const layer = store.project.layers.find((l) => l.id === shapeDrag!.layerId);
      if (!layer || layer.type !== 'vector') return;
      const shape = layer.params.shapes.find((s) => s.id === shapeDrag!.shapeId);
      if (!shape) return;
      const tr = resolveLayerTransform(layer, store.playhead, canvas.width, canvas.height);
      const local = screenToLayerLocal(pt.x, pt.y, tr);
      applyShapeMove(shape, shapeDrag.snapshot, local.x - shapeDrag.startLocal.x, local.y - shapeDrag.startLocal.y);
      return;
    }
    if (!dragMode || !dragLayerId || !dragTr) return;
    const layer = store.project.layers.find((l) => l.id === dragLayerId);
    if (!layer) return;
    if (dragMode === 'move') {
      // Move in the layer's OWN pre-drag local frame, then convert that
      // local delta back to the x/y unit space tracks are stored in
      // (dividing out the same W/270,H/480 factor renderLayerAt multiplies
      // by) — keeps the drag feeling 1:1 regardless of aspect/canvas size.
      const localNow = screenToLayerLocal(pt.x, pt.y, dragTr);
      const dLocalX = localNow.x - dragStartLocal.x;
      const dLocalY = localNow.y - dragStartLocal.y;
      const cos = Math.cos(dragTr.rotation), sin = Math.sin(dragTr.rotation);
      const worldDx = (dLocalX * cos - dLocalY * sin) * dragTr.worldScale;
      const worldDy = (dLocalX * sin + dLocalY * cos) * dragTr.worldScale;
      const newXUnits = (dragTr.x + worldDx) / (canvas.width / 270);
      const newYUnits = (dragTr.y + worldDy) / (canvas.height / 480);
      writeValue(layer, 'x', kfIds.x ?? null, newXUnits);
      writeValue(layer, 'y', kfIds.y ?? null, newYUnits);
    } else if (dragMode === 'scale') {
      const d0 = screenDist(dragStartCanvas.x, dragStartCanvas.y, dragTr.x, dragTr.y);
      const d1 = screenDist(pt.x, pt.y, dragTr.x, dragTr.y);
      const factor = d0 > 1 ? d1 / d0 : 1;
      const baseScale = evalScaleAtDragStart(layer);
      writeValue(layer, 'scale', kfIds.scale ?? null, Math.max(0.01, baseScale * factor));
    } else if (dragMode === 'rotate') {
      const a0 = Math.atan2(dragStartCanvas.y - dragTr.y, dragStartCanvas.x - dragTr.x);
      const a1 = Math.atan2(pt.y - dragTr.y, pt.x - dragTr.x);
      const baseRotation = evalRotationAtDragStart(layer);
      writeValue(layer, 'rotation', kfIds.rotation ?? null, baseRotation + (a1 - a0));
    }
  }

  // Captured once, at drag start, so a scale/rotate drag reads a stable base
  // value instead of re-evaluating (and drifting against) a live keyframe.
  let scaleAtStart = 1;
  let rotationAtStart = 0;
  function evalScaleAtDragStart(_layer: AnyLayer): number {
    return scaleAtStart;
  }
  function evalRotationAtDragStart(_layer: AnyLayer): number {
    return rotationAtStart;
  }

  function onSelectPointerUp() {
    if (shapeDrag) {
      store.commit();
      shapeDrag = null;
      return;
    }
    if (dragMode) store.commit();
    dragMode = null;
    dragLayerId = null;
    dragTr = null;
    kfIds = {};
  }

  // ---- shape tools (rect/ellipse/line/polygon) ----
  let drawingShapeId: string | null = null;
  let drawingLayerId: string | null = null;
  let drawStartLocal = { x: 0, y: 0 };
  let drawTr: LayerTransform | null = null;

  function targetVectorLayer(): AnyLayer {
    const sel = selectedLayer();
    if (sel && sel.type === 'vector' && !sel.locked) return sel;
    return store.addLayer('vector');
  }

  function baseShapeFields() {
    return {
      id: crypto.randomUUID(),
      fill: { kind: 'flat' as const, color: toolState.fillColor },
      stroke: toolState.strokeColor,
      strokeWidth: toolState.strokeWidth,
    };
  }

  function onShapePointerDown(pt: { x: number; y: number }) {
    const layer = targetVectorLayer();
    if (layer.type !== 'vector') return;
    drawTr = resolveLayerTransform(layer, store.playhead, canvas.width, canvas.height);
    drawStartLocal = screenToLayerLocal(pt.x, pt.y, drawTr);
    drawingLayerId = layer.id;
    let shape: VectorShape;
    if (toolState.active === 'rect') {
      shape = { ...baseShapeFields(), kind: 'rect', x: drawStartLocal.x, y: drawStartLocal.y, w: 0, h: 0, rotation: 0 };
    } else if (toolState.active === 'ellipse') {
      shape = { ...baseShapeFields(), kind: 'ellipse', x: drawStartLocal.x, y: drawStartLocal.y, rx: 0, ry: 0, rotation: 0 };
    } else if (toolState.active === 'line') {
      shape = { ...baseShapeFields(), kind: 'line', x1: drawStartLocal.x, y1: drawStartLocal.y, x2: drawStartLocal.x, y2: drawStartLocal.y };
    } else {
      shape = { ...baseShapeFields(), kind: 'polygon', x: drawStartLocal.x, y: drawStartLocal.y, sides: 5, radius: 0, rotation: 0 };
    }
    layer.params.shapes.push(shape);
    drawingShapeId = shape.id;
  }

  function onShapePointerMove(pt: { x: number; y: number }) {
    if (!drawingShapeId || !drawingLayerId || !drawTr) return;
    const layer = store.project.layers.find((l) => l.id === drawingLayerId);
    if (!layer || layer.type !== 'vector') return;
    const shape = layer.params.shapes.find((s) => s.id === drawingShapeId);
    if (!shape) return;
    const local = screenToLayerLocal(pt.x, pt.y, drawTr);
    if (shape.kind === 'rect') {
      shape.x = Math.min(drawStartLocal.x, local.x);
      shape.y = Math.min(drawStartLocal.y, local.y);
      shape.w = Math.abs(local.x - drawStartLocal.x);
      shape.h = Math.abs(local.y - drawStartLocal.y);
    } else if (shape.kind === 'ellipse') {
      shape.rx = Math.abs(local.x - drawStartLocal.x);
      shape.ry = Math.abs(local.y - drawStartLocal.y);
    } else if (shape.kind === 'line') {
      shape.x2 = local.x;
      shape.y2 = local.y;
    } else if (shape.kind === 'polygon') {
      shape.radius = screenDist(local.x, local.y, drawStartLocal.x, drawStartLocal.y);
    }
  }

  function onShapePointerUp() {
    if (drawingShapeId) store.commit();
    drawingShapeId = null;
    drawingLayerId = null;
    drawTr = null;
  }

  // ---- pen tool ----
  function onPenPointerDown(pt: { x: number; y: number }) {
    const layer = targetVectorLayer();
    if (layer.type !== 'vector') return;
    const tr = resolveLayerTransform(layer, store.playhead, canvas.width, canvas.height);
    const local = screenToLayerLocal(pt.x, pt.y, tr);

    let shape = layer.params.shapes.find((s) => s.id === toolState.editingShapeId);
    if (!shape || shape.kind !== 'path') {
      shape = { ...baseShapeFields(), kind: 'path', closed: false, points: [newAnchor(local.x, local.y)] };
      layer.params.shapes.push(shape);
      toolState.editingShapeId = shape.id;
      toolState.selectedAnchorId = shape.points[0].id;
      drawingLayerId = layer.id;
      pendingAnchorId = shape.points[0].id;
      penMode = 'handle';
      return;
    }
    drawingLayerId = layer.id;
    // Closing: click near the start anchor with enough points already placed.
    const first = shape.points[0];
    const firstScreen = layerLocalToScreen(first.x, first.y, tr);
    if (shape.points.length >= 3 && screenDist(pt.x, pt.y, firstScreen.x, firstScreen.y) < HANDLE_HIT_PX) {
      shape.closed = true;
      toolState.editingShapeId = null;
      toolState.selectedAnchorId = null;
      store.commit();
      return;
    }
    // Re-selecting/dragging an existing anchor.
    for (const anchor of shape.points) {
      const s = layerLocalToScreen(anchor.x, anchor.y, tr);
      if (screenDist(pt.x, pt.y, s.x, s.y) < HANDLE_HIT_PX) {
        toolState.selectedAnchorId = anchor.id;
        pendingAnchorId = anchor.id;
        penMode = 'move';
        return;
      }
    }
    // Otherwise: append a new anchor.
    const anchor = newAnchor(local.x, local.y);
    shape.points.push(anchor);
    toolState.selectedAnchorId = anchor.id;
    pendingAnchorId = anchor.id;
    penMode = 'handle';
  }

  let pendingAnchorId: string | null = null;
  let penMode: 'handle' | 'move' | null = null;

  function onPenPointerMove(pt: { x: number; y: number }, altKey: boolean) {
    if (!pendingAnchorId || !drawingLayerId) return;
    const layer = store.project.layers.find((l) => l.id === drawingLayerId);
    if (!layer || layer.type !== 'vector') return;
    const shape = layer.params.shapes.find((s) => s.id === toolState.editingShapeId);
    if (!shape || shape.kind !== 'path') return;
    const anchor = shape.points.find((a) => a.id === pendingAnchorId);
    if (!anchor) return;
    const tr = resolveLayerTransform(layer, store.playhead, canvas.width, canvas.height);
    const local = screenToLayerLocal(pt.x, pt.y, tr);
    if (penMode === 'move') {
      anchor.x = local.x;
      anchor.y = local.y;
    } else if (penMode === 'handle') {
      const dx = local.x - anchor.x;
      const dy = local.y - anchor.y;
      anchor.handleOut = { x: dx, y: dy };
      if (!altKey) {
        anchor.handleIn = { x: -dx, y: -dy };
        anchor.mirrored = true;
      } else {
        anchor.mirrored = false;
      }
    }
  }

  function onPenPointerUp() {
    if (pendingAnchorId) store.commit();
    pendingAnchorId = null;
    penMode = null;
    drawingLayerId = null;
  }

  // ---- node tool (edit an EXISTING path's anchors — move points, and turn a
  // corner into a curve by Alt-dragging a handle out of it — without the pen
  // tool's "click empty space appends a new anchor" behavior) ----
  type NodeDragMode = 'moveAnchor' | 'addHandle' | 'dragHandleOut' | 'dragHandleIn' | null;
  let nodeDragMode: NodeDragMode = null;
  let nodeDragAnchorId: string | null = null;
  let nodeDragLayerId: string | null = null;

  // Finds the path shape the node tool should operate on: whichever is
  // already being edited (toolState.editingShapeId), or — if nothing is —
  // whatever path shape the click landed on, which starts editing it (same
  // re-entry rule the pen tool uses). Each return is typed/narrowed locally,
  // right where the 'path' check happens, rather than through a reassigned
  // `let` — avoids the union-narrowing gap noted elsewhere in this codebase
  // where TS loses track of a shape kind across a conditional reassignment.
  function resolveEditableShape(pt: { x: number; y: number }): { layer: AnyLayer; shape: VectorPathShape } | null {
    const layer = selectedLayer();
    if (layer && layer.type === 'vector') {
      const shape = layer.params.shapes.find((s) => s.id === toolState.editingShapeId);
      if (shape && shape.kind === 'path') return { layer, shape };
    }
    const hit = pickShapeAt(pt);
    if (hit && hit.shape.kind === 'path') {
      store.selection.selectLayer(hit.layer.id);
      toolState.editingShapeId = hit.shape.id;
      return { layer: hit.layer, shape: hit.shape };
    }
    return null;
  }

  function onNodePointerDown(pt: { x: number; y: number }, altKey: boolean) {
    const target = resolveEditableShape(pt);
    if (!target) {
      toolState.editingShapeId = null;
      toolState.selectedAnchorId = null;
      return;
    }
    const { layer, shape } = target;
    const tr = resolveLayerTransform(layer, store.playhead, canvas.width, canvas.height);
    nodeDragLayerId = layer.id;
    // Handle dots take priority over anchors so a handle sitting close to its
    // own anchor is still reachable.
    for (const anchor of shape.points) {
      if (anchor.handleOut) {
        const h = layerLocalToScreen(anchor.x + anchor.handleOut.x, anchor.y + anchor.handleOut.y, tr);
        if (screenDist(pt.x, pt.y, h.x, h.y) < HANDLE_HIT_PX) {
          toolState.selectedAnchorId = anchor.id;
          nodeDragAnchorId = anchor.id;
          nodeDragMode = 'dragHandleOut';
          return;
        }
      }
      if (anchor.handleIn) {
        const h = layerLocalToScreen(anchor.x + anchor.handleIn.x, anchor.y + anchor.handleIn.y, tr);
        if (screenDist(pt.x, pt.y, h.x, h.y) < HANDLE_HIT_PX) {
          toolState.selectedAnchorId = anchor.id;
          nodeDragAnchorId = anchor.id;
          nodeDragMode = 'dragHandleIn';
          return;
        }
      }
    }
    for (const anchor of shape.points) {
      const s = layerLocalToScreen(anchor.x, anchor.y, tr);
      if (screenDist(pt.x, pt.y, s.x, s.y) < HANDLE_HIT_PX) {
        toolState.selectedAnchorId = anchor.id;
        nodeDragAnchorId = anchor.id;
        // Plain drag moves the point; Alt-drag pulls a fresh curve handle out
        // of it (turns a corner into a smooth curve point).
        nodeDragMode = altKey ? 'addHandle' : 'moveAnchor';
        return;
      }
    }
    // Clicked empty space inside the layer — deselect the anchor but keep
    // editing this shape (its anchors stay visible).
    toolState.selectedAnchorId = null;
  }

  function onNodePointerMove(pt: { x: number; y: number }, altKey: boolean) {
    if (!nodeDragMode || !nodeDragAnchorId || !nodeDragLayerId) return;
    const layer = store.project.layers.find((l) => l.id === nodeDragLayerId);
    if (!layer || layer.type !== 'vector') return;
    const shape = layer.params.shapes.find((s) => s.id === toolState.editingShapeId);
    if (!shape || shape.kind !== 'path') return;
    const anchor = shape.points.find((a) => a.id === nodeDragAnchorId);
    if (!anchor) return;
    const tr = resolveLayerTransform(layer, store.playhead, canvas.width, canvas.height);
    const local = screenToLayerLocal(pt.x, pt.y, tr);
    if (nodeDragMode === 'moveAnchor') {
      anchor.x = local.x;
      anchor.y = local.y;
    } else if (nodeDragMode === 'addHandle') {
      const dx = local.x - anchor.x, dy = local.y - anchor.y;
      anchor.handleOut = { x: dx, y: dy };
      anchor.handleIn = { x: -dx, y: -dy };
      anchor.mirrored = true;
    } else if (nodeDragMode === 'dragHandleOut') {
      const dx = local.x - anchor.x, dy = local.y - anchor.y;
      anchor.handleOut = { x: dx, y: dy };
      if (anchor.mirrored && !altKey) anchor.handleIn = { x: -dx, y: -dy };
      else anchor.mirrored = false;
    } else if (nodeDragMode === 'dragHandleIn') {
      const dx = local.x - anchor.x, dy = local.y - anchor.y;
      anchor.handleIn = { x: dx, y: dy };
      if (anchor.mirrored && !altKey) anchor.handleOut = { x: -dx, y: -dy };
      else anchor.mirrored = false;
    }
  }

  function onNodePointerUp() {
    if (nodeDragMode) store.commit();
    nodeDragMode = null;
    nodeDragAnchorId = null;
    nodeDragLayerId = null;
  }

  function deleteSelectedAnchor() {
    if ((toolState.active !== 'pen' && toolState.active !== 'node') || !toolState.editingShapeId || !toolState.selectedAnchorId) return;
    const layer = selectedLayer();
    if (!layer || layer.type !== 'vector') return;
    const shape = layer.params.shapes.find((s) => s.id === toolState.editingShapeId);
    if (!shape || shape.kind !== 'path') return;
    shape.points = shape.points.filter((a) => a.id !== toolState.selectedAnchorId);
    toolState.selectedAnchorId = null;
    if (!shape.points.length) {
      layer.params.shapes = layer.params.shapes.filter((s) => s.id !== shape.id);
      toolState.editingShapeId = null;
    }
    store.commit();
  }

  function onKeydown(e: KeyboardEvent) {
    if (e.code === 'Space' && !isTextInput(e.target) && !spaceHeld) {
      spaceHeld = true;
      e.preventDefault(); // don't let Space also scroll the dockview panel
    }
    if (toolState.active !== 'pen' && toolState.active !== 'node') return;
    if (e.key === 'Escape') {
      toolState.editingShapeId = null;
      toolState.selectedAnchorId = null;
    } else if (e.key === 'Delete' || e.key === 'Backspace') {
      deleteSelectedAnchor();
    }
  }

  // ---- paintbrush (bitmap layers) ----
  let brushLast: { x: number; y: number } | null = null;
  let brushLayerId: string | null = null;
  let brushOffscreen: HTMLCanvasElement | null = null;

  function stampBrush(bctx: CanvasRenderingContext2D, x: number, y: number) {
    const size = toolState.brushSize;
    bctx.save();
    bctx.globalAlpha = toolState.brushOpacity;
    if (toolState.brushHardness >= 0.98) {
      bctx.fillStyle = toolState.brushColor;
      bctx.beginPath();
      bctx.arc(x, y, size / 2, 0, Math.PI * 2);
      bctx.fill();
    } else {
      const g = bctx.createRadialGradient(x, y, 0, x, y, size / 2);
      g.addColorStop(0, toolState.brushColor);
      g.addColorStop(Math.max(0.05, toolState.brushHardness), toolState.brushColor);
      g.addColorStop(1, 'transparent');
      bctx.fillStyle = g;
      bctx.beginPath();
      bctx.arc(x, y, size / 2, 0, Math.PI * 2);
      bctx.fill();
    }
    bctx.restore();
  }

  function onBrushPointerDown(pt: { x: number; y: number }) {
    const layer = selectedLayer();
    if (!layer || layer.type !== 'bitmap' || layer.locked) {
      toast.push('error', 'Select an image layer to paint on it');
      return;
    }
    brushLayerId = layer.id;
    // Bitmap layers have no authored size — the offscreen paint buffer is
    // exactly the (live Studio) canvas resolution, matching how render.js
    // now draws bitmap layers full-bleed at W×H (see layerLocalBounds's doc).
    brushOffscreen = document.createElement('canvas');
    brushOffscreen.width = canvas.width;
    brushOffscreen.height = canvas.height;
    const bctx = brushOffscreen.getContext('2d')!;
    if (layer.params.dataUrl) {
      const img = new Image();
      img.src = layer.params.dataUrl;
      if (img.complete) bctx.drawImage(img, 0, 0, canvas.width, canvas.height);
    }
    const tr = resolveLayerTransform(layer, store.playhead, canvas.width, canvas.height);
    const local = screenToLayerLocal(pt.x, pt.y, tr);
    const bx = local.x + canvas.width / 2, by = local.y + canvas.height / 2;
    stampBrush(bctx, bx, by);
    brushLast = { x: bx, y: by };
  }

  function onBrushPointerMove(pt: { x: number; y: number }) {
    if (!brushOffscreen || !brushLayerId || !brushLast) return;
    const layer = store.project.layers.find((l) => l.id === brushLayerId);
    if (!layer || layer.type !== 'bitmap') return;
    const bctx = brushOffscreen.getContext('2d')!;
    const tr = resolveLayerTransform(layer, store.playhead, canvas.width, canvas.height);
    const local = screenToLayerLocal(pt.x, pt.y, tr);
    const bx = local.x + canvas.width / 2, by = local.y + canvas.height / 2;
    const dist = screenDist(bx, by, brushLast.x, brushLast.y);
    const steps = Math.max(1, Math.floor(dist / (toolState.brushSize / 4)));
    for (let i = 1; i <= steps; i++) {
      stampBrush(bctx, brushLast.x + ((bx - brushLast.x) * i) / steps, brushLast.y + ((by - brushLast.y) * i) / steps);
    }
    brushLast = { x: bx, y: by };
  }

  function onBrushPointerUp() {
    if (brushOffscreen && brushLayerId) {
      const layer = store.project.layers.find((l) => l.id === brushLayerId);
      if (layer && layer.type === 'bitmap') {
        layer.params.dataUrl = brushOffscreen.toDataURL('image/png');
        store.commit();
      }
    }
    brushOffscreen = null;
    brushLayerId = null;
    brushLast = null;
  }

  // ---- dispatch ----
  function onPointerDown(e: PointerEvent) {
    const pt = toCanvasPoint(e);
    if (toolState.active === 'select') onSelectPointerDown(pt);
    else if (toolState.active === 'pen') onPenPointerDown(pt);
    else if (toolState.active === 'node') onNodePointerDown(pt, e.altKey);
    else if (toolState.active === 'brush') onBrushPointerDown(pt);
    else onShapePointerDown(pt);
  }
  function onPointerMove(e: PointerEvent) {
    const pt = toCanvasPoint(e);
    if (toolState.active === 'select') onSelectPointerMove(pt);
    else if (toolState.active === 'pen') onPenPointerMove(pt, e.altKey);
    else if (toolState.active === 'node') onNodePointerMove(pt, e.altKey);
    else if (toolState.active === 'brush') onBrushPointerMove(pt);
    else onShapePointerMove(pt);
  }
  function onPointerUp() {
    if (toolState.active === 'select') onSelectPointerUp();
    else if (toolState.active === 'pen') onPenPointerUp();
    else if (toolState.active === 'node') onNodePointerUp();
    else if (toolState.active === 'brush') onBrushPointerUp();
    else onShapePointerUp();
  }

  // A shape selection only ever makes sense within the layer it belongs to,
  // and only as long as the shape itself still exists — reruns whenever the
  // layer selection changes (LayersPanel, Tab-to-cycle, etc.) or the shape
  // list changes (deleted via Delete, Rasterize, ShapeFields' Delete button),
  // and drops a now-stale id instead of silently pointing at nothing/the
  // wrong layer. Note this does NOT fire when onSelectPointerDown sets both
  // selection.layerId and selectedShapeId together in the same tick — by the
  // time this effect runs, that pair is already self-consistent.
  $effect(() => {
    const id = toolState.selectedShapeId;
    if (!id) return;
    const layer = store.project.layers.find((l) => l.id === store.selection.layerId);
    const stillValid = !!layer && layer.type === 'vector' && layer.params.shapes.some((s) => s.id === id);
    if (!stillValid) toolState.selectedShapeId = null;
  });

  // Capture scale/rotation-at-drag-start right when a drag begins, not lazily
  // (evalScaleAtDragStart/evalRotationAtDragStart read these) — set here
  // rather than inline in onSelectPointerDown to keep that function's early
  // returns simple.
  $effect(() => {
    if (dragMode === 'scale' || dragMode === 'rotate') {
      const layer = store.project.layers.find((l) => l.id === dragLayerId);
      if (layer) {
        scaleAtStart = layer.statics.scale;
        rotationAtStart = layer.statics.rotation;
        if (kfIds.scale) {
          const kf = layer.tracks.scale.find((k) => k.id === kfIds.scale);
          if (kf) scaleAtStart = kf.v;
        }
        if (kfIds.rotation) {
          const kf = layer.tracks.rotation.find((k) => k.id === kfIds.rotation);
          if (kf) rotationAtStart = kf.v;
        }
      }
    }
  });
</script>

<svelte:window onkeydown={onKeydown} onkeyup={onWindowKeyup} />

<!-- A canvas-editor surface, not a semantic widget any ARIA role fits well —
     same pattern already used for the scrim/canvas-like interactive divs
     elsewhere in this app (ScriptEditorModal, LyricsImporter). -->
<!-- svelte-ignore a11y_no_static_element_interactions -->
<div
  class="workspaceCanvasWrap"
  class:spacePan={spaceHeld && !panDragging}
  class:panning={panDragging}
  class:rotating={rotateDragging}
  bind:this={wrapEl}
  onwheel={onWrapWheel}
  onpointerdown={onWrapPointerDown}
  onpointermove={onWrapPointerMove}
  onpointerup={onWrapPointerUp}
  onpointerleave={onWrapPointerUp}
  use:assetDrop={{
    accept: (e) => e.kind === 'image' || e.kind === 'svg',
    onAsset: (e) => addImageLayerFromAsset(store, e),
  }}
>
  <canvas
    bind:this={canvas}
    style="transform: translate({workspaceView.panX}px, {workspaceView.panY}px) rotate({workspaceView.rotation}rad) scale({workspaceView.zoom});"
  ></canvas>

  <div class="viewControls">
    <button class="small ghost" title="Zoom out" aria-label="Zoom out" onclick={() => workspaceView.zoomStep(-1)}><ZoomOut size={13} /></button>
    <span class="zoomLabel">{Math.round(workspaceView.zoom * 100)}%</span>
    <button class="small ghost" title="Zoom in" aria-label="Zoom in" onclick={() => workspaceView.zoomStep(1)}><ZoomIn size={13} /></button>
    <span class="sep"></span>
    <button class="small ghost" title="Reset view (100%, centered, unrotated)" aria-label="Reset view" onclick={() => workspaceView.reset()}>
      <RotateCcw size={13} />
    </button>
  </div>
</div>

<style>
  .workspaceCanvasWrap {
    position: relative;
    width: 100%;
    height: 100%;
    overflow: hidden;
    background: #fff;
    display: flex;
    align-items: center;
    justify-content: center;
    touch-action: none;
  }
  .workspaceCanvasWrap.spacePan,
  .workspaceCanvasWrap.panning {
    cursor: grab;
  }
  .workspaceCanvasWrap.panning {
    cursor: grabbing;
  }
  .workspaceCanvasWrap.rotating {
    cursor: alias;
  }
  .workspaceCanvasWrap:global(.dragOver) {
    outline: 2px dashed var(--accent);
    outline-offset: -2px;
  }
  canvas {
    display: block;
    flex-shrink: 0;
    transform-origin: center center;
    /* The transparent-PNG-style checkerboard — anything the composited
       render leaves transparent (drawPreviewFrame's transparentBg option)
       shows this through instead of the real export's opaque black, and the
       shadow gives the canvas bounds a crisp, unambiguous edge against the
       white pasteboard around it. */
    background-color: #fff;
    background-image:
      linear-gradient(45deg, #ccc 25%, transparent 25%),
      linear-gradient(-45deg, #ccc 25%, transparent 25%),
      linear-gradient(45deg, transparent 75%, #ccc 75%),
      linear-gradient(-45deg, transparent 75%, #ccc 75%);
    background-size: 16px 16px;
    background-position: 0 0, 0 8px, 8px -8px, -8px 0px;
    box-shadow: 0 0 0 1px rgba(0, 0, 0, 0.35), 0 4px 24px rgba(0, 0, 0, 0.18);
  }
  .viewControls {
    position: absolute;
    left: 8px;
    bottom: 8px;
    display: flex;
    align-items: center;
    gap: 4px;
    padding: 3px 6px;
    background: var(--bg1);
    border: 1px solid var(--line);
    border-radius: 4px;
  }
  .zoomLabel {
    font: 10px var(--mono);
    color: var(--dim);
    width: 34px;
    text-align: center;
  }
  .viewControls .sep {
    width: 1px;
    align-self: stretch;
    background: var(--line);
    margin: 0 2px;
  }
</style>
