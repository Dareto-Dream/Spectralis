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
  import { toolState } from '../state/toolState.svelte';
  import { resolveLayerTransform, screenToLayerLocal, layerLocalToScreen, layerLocalBounds, screenDist, newAnchor, type LayerTransform } from '../lib/vectorHitTest';
  import type { AnimKey, AnyLayer, VectorShape } from '../types/project';
  import { toast } from '../state/toast.svelte';

  let { store, audio }: { store: ProjectStore; audio: AudioState } = $props();

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
    const b = layerLocalBounds(layer);
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

  function drawPenOverlay() {
    if (toolState.active !== 'pen' || !toolState.editingShapeId) return;
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

  function tick(now: number) {
    raf = requestAnimationFrame(tick);
    const level = audio.loaded ? audio.currentLevel() : { peak: 0, rms: 0 };
    drawPreviewFrame(ctx, canvas.width, canvas.height, store.project, store.playhead, now, level, frameState, store.soloedLayerIds);
    drawSelectionOverlay();
    drawPenOverlay();
  }

  onMount(() => {
    ctx = canvas.getContext('2d')!;
    raf = requestAnimationFrame(tick);
  });
  onDestroy(() => cancelAnimationFrame(raf));

  // ---- pointer -> canvas-pixel coordinates ----
  function toCanvasPoint(e: PointerEvent): { x: number; y: number } {
    const rect = canvas.getBoundingClientRect();
    const sx = canvas.width / rect.width;
    const sy = canvas.height / rect.height;
    return { x: (e.clientX - rect.left) * sx, y: (e.clientY - rect.top) * sy };
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
    const b = layerLocalBounds(layer);
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

  function pickLayerAt(pt: { x: number; y: number }): AnyLayer | undefined {
    for (let i = store.project.layers.length - 1; i >= 0; i--) {
      const layer = store.project.layers[i];
      if (layer.locked) continue;
      const tr = resolveLayerTransform(layer, store.playhead, canvas.width, canvas.height);
      const local = screenToLayerLocal(pt.x, pt.y, tr);
      const b = layerLocalBounds(layer);
      if (local.x >= b.x && local.x <= b.x + b.w && local.y >= b.y && local.y <= b.y + b.h) return layer;
    }
    return undefined;
  }

  function onSelectPointerDown(pt: { x: number; y: number }) {
    const layer = selectedLayer();
    if (layer) {
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
      const local = screenToLayerLocal(pt.x, pt.y, tr);
      const b = layerLocalBounds(layer);
      if (!layer.locked && local.x >= b.x && local.x <= b.x + b.w && local.y >= b.y && local.y <= b.y + b.h) {
        dragMode = 'move';
        dragLayerId = layer.id;
        dragTr = tr;
        dragStartCanvas = pt;
        dragStartLocal = local;
        kfIds = { x: ensureKeyframeId(layer, 'x'), y: ensureKeyframeId(layer, 'y') };
        return;
      }
    }
    const hit = pickLayerAt(pt);
    store.selection.selectLayer(hit?.id ?? null);
  }

  function onSelectPointerMove(pt: { x: number; y: number }) {
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

  function deleteSelectedAnchor() {
    if (toolState.active !== 'pen' || !toolState.editingShapeId || !toolState.selectedAnchorId) return;
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
    if (toolState.active !== 'pen') return;
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
    brushOffscreen = document.createElement('canvas');
    brushOffscreen.width = layer.params.w;
    brushOffscreen.height = layer.params.h;
    const bctx = brushOffscreen.getContext('2d')!;
    if (layer.params.dataUrl) {
      const img = new Image();
      img.src = layer.params.dataUrl;
      if (img.complete) bctx.drawImage(img, 0, 0, layer.params.w, layer.params.h);
    }
    const tr = resolveLayerTransform(layer, store.playhead, canvas.width, canvas.height);
    const local = screenToLayerLocal(pt.x, pt.y, tr);
    const bx = local.x + layer.params.w / 2, by = local.y + layer.params.h / 2;
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
    const bx = local.x + layer.params.w / 2, by = local.y + layer.params.h / 2;
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
    else if (toolState.active === 'brush') onBrushPointerDown(pt);
    else onShapePointerDown(pt);
  }
  function onPointerMove(e: PointerEvent) {
    const pt = toCanvasPoint(e);
    if (toolState.active === 'select') onSelectPointerMove(pt);
    else if (toolState.active === 'pen') onPenPointerMove(pt, e.altKey);
    else if (toolState.active === 'brush') onBrushPointerMove(pt);
    else onShapePointerMove(pt);
  }
  function onPointerUp() {
    if (toolState.active === 'select') onSelectPointerUp();
    else if (toolState.active === 'pen') onPenPointerUp();
    else if (toolState.active === 'brush') onBrushPointerUp();
    else onShapePointerUp();
  }

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

<svelte:window onkeydown={onKeydown} />

<div class="workspaceCanvasWrap">
  <canvas
    bind:this={canvas}
    onpointerdown={onPointerDown}
    onpointermove={onPointerMove}
    onpointerup={onPointerUp}
    onpointerleave={onPointerUp}
  ></canvas>
</div>

<style>
  .workspaceCanvasWrap {
    position: relative;
    display: inline-block;
    background: #000;
  }
  canvas {
    display: block;
    touch-action: none;
  }
</style>
