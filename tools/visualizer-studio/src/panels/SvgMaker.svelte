<script lang="ts">
  import type { Component } from 'svelte';
  import { assetLibrary } from '../state/assetLibrary.svelte';
  import { toast } from '../state/toast.svelte';
  import { serializeSvgDocument, type Shape, type ShapeType } from '../lib/svgShapes';
  import MousePointer2 from '@lucide/svelte/icons/mouse-pointer-2';
  import Square from '@lucide/svelte/icons/square';
  import Circle from '@lucide/svelte/icons/circle';
  import Minus from '@lucide/svelte/icons/minus';
  import PenTool from '@lucide/svelte/icons/pen-tool';
  import Type from '@lucide/svelte/icons/type';
  import Trash2 from '@lucide/svelte/icons/trash-2';
  import Save from '@lucide/svelte/icons/save';
  import X from '@lucide/svelte/icons/x';
  import ChevronUp from '@lucide/svelte/icons/chevron-up';
  import ChevronDown from '@lucide/svelte/icons/chevron-down';

  // A basic-but-real vector doodle tool, not a full Illustrator: shapes,
  // freehand paths, and text, with a flat property editor and no undo stack of
  // its own — "draw an icon-sized accent asset and drop it into the project",
  // not general vector authoring. Output serializes straight to a standalone
  // SVG string and lands in the Assets docker, draggable everywhere else.
  type Tool = 'select' | ShapeType;
  const TOOLS: { id: Tool; icon: Component<{ size?: number }>; label: string }[] = [
    { id: 'select', icon: MousePointer2, label: 'Select' },
    { id: 'rect', icon: Square, label: 'Rectangle' },
    { id: 'ellipse', icon: Circle, label: 'Ellipse' },
    { id: 'line', icon: Minus, label: 'Line' },
    { id: 'path', icon: PenTool, label: 'Freehand' },
    { id: 'text', icon: Type, label: 'Text' },
  ];

  let width = $state(256);
  let height = $state(256);
  let shapes: Shape[] = $state([]);
  let tool: Tool = $state('select');
  let selectedId: string | null = $state(null);
  let fill = $state('#7fb7ff');
  let stroke = $state('#e7e7ee');
  let strokeWidth = $state(2);
  let assetName = $state('my-icon');

  let svgEl: SVGSVGElement | undefined = $state();
  let drawingId: string | null = null;
  let dragStart: { x: number; y: number } | null = null;

  const selected = $derived(shapes.find((s) => s.id === selectedId) ?? null);

  function toSvgPoint(e: PointerEvent): { x: number; y: number } {
    const rect = svgEl!.getBoundingClientRect();
    return {
      x: ((e.clientX - rect.left) / rect.width) * width,
      y: ((e.clientY - rect.top) / rect.height) * height,
    };
  }

  function selectOnly(id: string) {
    if (tool !== 'select') return;
    selectedId = id;
  }

  function onPointerDown(e: PointerEvent) {
    if (tool === 'select') return;
    const p = toSvgPoint(e);
    const base = { id: crypto.randomUUID(), fill, stroke, strokeWidth };
    let shape: Shape;
    if (tool === 'rect') shape = { ...base, type: 'rect', x: p.x, y: p.y, w: 0, h: 0 };
    else if (tool === 'ellipse') shape = { ...base, type: 'ellipse', cx: p.x, cy: p.y, rx: 0, ry: 0 };
    else if (tool === 'line') shape = { ...base, type: 'line', x1: p.x, y1: p.y, x2: p.x, y2: p.y };
    else if (tool === 'path') shape = { ...base, type: 'path', d: `M ${p.x.toFixed(1)} ${p.y.toFixed(1)}` };
    else {
      shape = { ...base, type: 'text', x: p.x, y: p.y, text: 'Text', fontSize: 24 };
      shapes.push(shape);
      selectedId = shape.id;
      tool = 'select';
      return;
    }
    dragStart = p;
    shapes.push(shape);
    drawingId = shape.id;
    selectedId = shape.id;
  }

  function onPointerMove(e: PointerEvent) {
    if (!drawingId || !dragStart) return;
    const p = toSvgPoint(e);
    const shape = shapes.find((s) => s.id === drawingId);
    if (!shape) return;
    if (shape.type === 'rect') {
      shape.x = Math.min(dragStart.x, p.x);
      shape.y = Math.min(dragStart.y, p.y);
      shape.w = Math.abs(p.x - dragStart.x);
      shape.h = Math.abs(p.y - dragStart.y);
    } else if (shape.type === 'ellipse') {
      shape.cx = dragStart.x;
      shape.cy = dragStart.y;
      shape.rx = Math.abs(p.x - dragStart.x);
      shape.ry = Math.abs(p.y - dragStart.y);
    } else if (shape.type === 'line') {
      shape.x2 = p.x;
      shape.y2 = p.y;
    } else if (shape.type === 'path') {
      shape.d += ` L ${p.x.toFixed(1)} ${p.y.toFixed(1)}`;
    }
  }

  function onPointerUp() {
    if (drawingId) tool = 'select';
    drawingId = null;
    dragStart = null;
  }

  function deleteSelected() {
    if (!selectedId) return;
    shapes = shapes.filter((s) => s.id !== selectedId);
    selectedId = null;
  }

  async function clearAll() {
    shapes = [];
    selectedId = null;
  }

  function moveZ(dir: -1 | 1) {
    if (!selectedId) return;
    const idx = shapes.findIndex((s) => s.id === selectedId);
    const swap = idx + dir;
    if (idx < 0 || swap < 0 || swap >= shapes.length) return;
    [shapes[idx], shapes[swap]] = [shapes[swap], shapes[idx]];
  }

  function saveToAssets() {
    if (!shapes.length) {
      toast.push('error', 'Draw something first');
      return;
    }
    const svg = serializeSvgDocument($state.snapshot(shapes) as Shape[], width, height);
    const name = assetName.trim() || 'icon';
    assetLibrary.addSvg(name, svg);
    toast.push('success', `Saved "${name}.svg" to Assets — drag it in from the Assets docker`);
  }
</script>

<div class="svgMaker">
  <div class="toolbar">
    {#each TOOLS as t (t.id)}
      <button class="icon" class:active={tool === t.id} title={t.label} aria-label={t.label} onclick={() => (tool = t.id)}>
        <t.icon size={14} />
      </button>
    {/each}
    <span class="sep"></span>
    <label class="dim">W<input type="number" min="8" max="2048" bind:value={width} /></label>
    <label class="dim">H<input type="number" min="8" max="2048" bind:value={height} /></label>
    <span class="spacer"></span>
    <button class="small ghost" onclick={clearAll} title="Clear artboard"><Trash2 size={12} /> Clear</button>
  </div>
  <div class="body">
    <div class="artboardOuter">
      <div class="artboardWrap" style="aspect-ratio: {width} / {height}">
        <!-- svelte-ignore a11y_no_static_element_interactions -->
        <svg
          bind:this={svgEl}
          viewBox="0 0 {width} {height}"
          onpointerdown={onPointerDown}
          onpointermove={onPointerMove}
          onpointerup={onPointerUp}
          onpointerleave={onPointerUp}
        >
          {#each shapes as s (s.id)}
            <!-- svelte-ignore a11y_click_events_have_key_events -->
            <!-- svelte-ignore a11y_no_static_element_interactions -->
            {#if s.type === 'rect'}
              <rect
                x={s.x} y={s.y} width={s.w} height={s.h}
                fill={s.fill} stroke={s.stroke} stroke-width={s.strokeWidth}
                class:selected={s.id === selectedId}
                onclick={() => selectOnly(s.id)}
              />
            {:else if s.type === 'ellipse'}
              <ellipse
                cx={s.cx} cy={s.cy} rx={s.rx} ry={s.ry}
                fill={s.fill} stroke={s.stroke} stroke-width={s.strokeWidth}
                class:selected={s.id === selectedId}
                onclick={() => selectOnly(s.id)}
              />
            {:else if s.type === 'line'}
              <line
                x1={s.x1} y1={s.y1} x2={s.x2} y2={s.y2}
                stroke={s.stroke} stroke-width={s.strokeWidth} stroke-linecap="round"
                class:selected={s.id === selectedId}
                onclick={() => selectOnly(s.id)}
              />
            {:else if s.type === 'path'}
              <path
                d={s.d} fill="none" stroke={s.stroke} stroke-width={s.strokeWidth}
                stroke-linecap="round" stroke-linejoin="round"
                class:selected={s.id === selectedId}
                onclick={() => selectOnly(s.id)}
              />
            {:else if s.type === 'text'}
              <text
                x={s.x} y={s.y} font-size={s.fontSize} fill={s.fill} stroke={s.stroke} stroke-width={s.strokeWidth}
                class:selected={s.id === selectedId}
                onclick={() => selectOnly(s.id)}
              >{s.text}</text>
            {/if}
          {/each}
        </svg>
      </div>
    </div>
    <div class="sidePanel">
      <div class="section">
        <h4>New Shape</h4>
        <label class="row"><span>Fill</span><input type="color" bind:value={fill} /></label>
        <label class="row"><span>Stroke</span><input type="color" bind:value={stroke} /></label>
        <label class="row"><span>Width</span><input type="number" min="0" max="40" step="0.5" bind:value={strokeWidth} /></label>
      </div>
      <div class="section">
        <h4>Selected</h4>
        {#if selected}
          {#if selected.type !== 'line' && selected.type !== 'path'}
            <label class="row"><span>Fill</span><input type="color" value={selected.fill} oninput={(e) => (selected.fill = (e.target as HTMLInputElement).value)} /></label>
          {/if}
          <label class="row"><span>Stroke</span><input type="color" value={selected.stroke} oninput={(e) => (selected.stroke = (e.target as HTMLInputElement).value)} /></label>
          <label class="row"><span>Width</span><input type="number" min="0" max="40" step="0.5" value={selected.strokeWidth} oninput={(e) => (selected.strokeWidth = +(e.target as HTMLInputElement).value)} /></label>
          {#if selected.type === 'text'}
            <label class="row"><span>Text</span><input type="text" value={selected.text} oninput={(e) => (selected.text = (e.target as HTMLInputElement).value)} /></label>
            <label class="row"><span>Size</span><input type="number" min="4" max="200" value={selected.fontSize} oninput={(e) => (selected.fontSize = +(e.target as HTMLInputElement).value)} /></label>
          {/if}
          <div class="propActions">
            <button class="icon ghost" title="Bring forward" aria-label="Bring forward" onclick={() => moveZ(1)}><ChevronUp size={13} /></button>
            <button class="icon ghost" title="Send backward" aria-label="Send backward" onclick={() => moveZ(-1)}><ChevronDown size={13} /></button>
            <button class="icon ghost danger" title="Delete shape" aria-label="Delete shape" onclick={deleteSelected}><X size={13} /></button>
          </div>
        {:else}
          <p class="hint">Pick a tool, drag on the artboard. Switch to Select and click a shape to edit it here.</p>
        {/if}
      </div>
      <div class="section shapeList">
        <h4>Shapes ({shapes.length})</h4>
        {#each shapes as s, i (s.id)}
          <button class="shapeRow" class:active={s.id === selectedId} onclick={() => { selectedId = s.id; tool = 'select'; }}>
            {s.type} #{i + 1}
          </button>
        {/each}
      </div>
      <div class="section saveSection">
        <input type="text" placeholder="asset name" bind:value={assetName} />
        <button class="small primary" onclick={saveToAssets}><Save size={12} /> Save to Assets</button>
      </div>
    </div>
  </div>
</div>

<style>
  .svgMaker {
    display: flex;
    flex-direction: column;
    height: 100%;
  }
  .toolbar {
    display: flex;
    align-items: center;
    gap: 4px;
    padding: 6px 8px;
    border-bottom: 1px solid var(--line);
    flex-shrink: 0;
  }
  .toolbar button.icon.active {
    background: var(--bg3);
    color: var(--accent);
  }
  .sep {
    width: 1px;
    align-self: stretch;
    background: var(--line);
    margin: 0 2px;
  }
  .dim {
    display: flex;
    align-items: center;
    gap: 3px;
    font: 10px var(--mono);
    color: var(--dim2);
  }
  .dim input {
    width: 48px;
  }
  .spacer {
    flex: 1;
  }
  .body {
    flex: 1;
    display: flex;
    min-height: 0;
  }
  .artboardOuter {
    flex: 1;
    display: flex;
    align-items: center;
    justify-content: center;
    overflow: auto;
    padding: 16px;
  }
  .artboardWrap {
    max-width: 100%;
    max-height: 100%;
    box-shadow: 0 0 0 1px var(--line);
    background-color: #1a1a1a;
    background-image:
      linear-gradient(45deg, #2a2a2a 25%, transparent 25%),
      linear-gradient(-45deg, #2a2a2a 25%, transparent 25%),
      linear-gradient(45deg, transparent 75%, #2a2a2a 75%),
      linear-gradient(-45deg, transparent 75%, #2a2a2a 75%);
    background-size: 16px 16px;
    background-position: 0 0, 0 8px, 8px -8px, -8px 0px;
  }
  .artboardWrap svg {
    width: 100%;
    height: 100%;
    display: block;
    cursor: crosshair;
  }
  .artboardWrap svg :global(.selected) {
    outline: 1.5px dashed var(--accent);
    outline-offset: 2px;
  }
  .sidePanel {
    width: 200px;
    flex-shrink: 0;
    border-left: 1px solid var(--line);
    overflow-y: auto;
    padding: 8px;
    display: flex;
    flex-direction: column;
    gap: 12px;
  }
  .section h4 {
    margin: 0 0 6px;
    font: 10px var(--mono);
    color: var(--dim2);
    text-transform: uppercase;
    letter-spacing: 0.05em;
  }
  .row {
    display: flex;
    align-items: center;
    justify-content: space-between;
    gap: 6px;
    font: 11px var(--mono);
    color: var(--dim);
    margin-bottom: 4px;
  }
  .row input[type='color'] {
    width: 40px;
    padding: 1px;
  }
  .row input[type='number'],
  .row input[type='text'] {
    width: 90px;
  }
  .hint {
    margin: 0;
    font: 10px var(--mono);
    color: var(--dim2);
    line-height: 1.4;
  }
  .propActions {
    display: flex;
    gap: 3px;
    margin-top: 4px;
  }
  .shapeList {
    display: flex;
    flex-direction: column;
    gap: 2px;
  }
  .shapeRow {
    text-align: left;
    background: none;
    border: none;
    color: var(--dim);
    font: 10px var(--mono);
    padding: 3px 4px;
    border-radius: 3px;
    text-transform: capitalize;
  }
  .shapeRow.active {
    background: var(--bg3);
    color: var(--accent);
  }
  .saveSection {
    margin-top: auto;
    display: flex;
    flex-direction: column;
    gap: 6px;
  }
</style>
