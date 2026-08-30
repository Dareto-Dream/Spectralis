<script lang="ts">
  // Properties for the individual shape currently selected on the Workspace
  // canvas (toolState.selectedShapeId, Select tool — see WorkspaceCanvas.svelte's
  // pickShapeAt). Distinct from ParamFields.svelte, which only ever shows the
  // layer-level hint text for vector/bitmap layers — a layer has no scalar
  // params of its own worth editing here, but an individual SHAPE inside a
  // vector layer very much does.
  import type { FillColor, VectorShape } from '../types/project';

  let { shape, onChange, onDelete }: {
    shape: VectorShape;
    onChange: () => void;
    onDelete: () => void;
  } = $props();

  function isSentinel(c: FillColor): c is '$hueA' | '$hueB' {
    return c === '$hueA' || c === '$hueB';
  }

  function num(e: Event): number {
    return parseFloat((e.target as HTMLInputElement).value) || 0;
  }

  function fillSelectValue(): string {
    if (shape.fill.kind !== 'flat') return 'custom';
    return isSentinel(shape.fill.color) ? shape.fill.color.slice(1) : 'custom';
  }
  function setFillMode(mode: string) {
    if (mode === 'hueA' || mode === 'hueB') shape.fill = { kind: 'flat', color: mode === 'hueA' ? '$hueA' : '$hueB' };
    else if (shape.fill.kind !== 'flat' || isSentinel(shape.fill.color)) shape.fill = { kind: 'flat', color: '#ffffff' };
    onChange();
  }
  function setFillColor(v: string) {
    shape.fill = { kind: 'flat', color: v };
    onChange();
  }
  function flattenGradient() {
    if (shape.fill.kind === 'flat') return;
    shape.fill = { kind: 'flat', color: shape.fill.stops[0]?.color ?? '#ffffff' };
    onChange();
  }

  function toggleGlow(on: boolean) {
    shape.glow = on ? { blur: shape.glow?.blur ?? 20, color: shape.glow?.color } : undefined;
    onChange();
  }
</script>

<div class="shapeFields">
  <div class="rowHead">
    <span class="kindLabel">{shape.kind} shape</span>
    <button class="small danger" onclick={onDelete}>Delete</button>
  </div>

  {#if shape.kind === 'rect'}
    <label class="field"><span>Width</span><input type="number" step="1" value={shape.w} onchange={(e) => { shape.w = num(e); onChange(); }} /></label>
    <label class="field"><span>Height</span><input type="number" step="1" value={shape.h} onchange={(e) => { shape.h = num(e); onChange(); }} /></label>
    <label class="field"><span>Rotation</span><input type="number" step="0.01" value={shape.rotation} onchange={(e) => { shape.rotation = num(e); onChange(); }} /></label>
  {:else if shape.kind === 'ellipse'}
    <label class="field"><span>Radius X</span><input type="number" step="1" value={shape.rx} onchange={(e) => { shape.rx = num(e); onChange(); }} /></label>
    <label class="field"><span>Radius Y</span><input type="number" step="1" value={shape.ry} onchange={(e) => { shape.ry = num(e); onChange(); }} /></label>
    <label class="field"><span>Rotation</span><input type="number" step="0.01" value={shape.rotation} onchange={(e) => { shape.rotation = num(e); onChange(); }} /></label>
  {:else if shape.kind === 'line'}
    <label class="field"><span>X1</span><input type="number" step="1" value={shape.x1} onchange={(e) => { shape.x1 = num(e); onChange(); }} /></label>
    <label class="field"><span>Y1</span><input type="number" step="1" value={shape.y1} onchange={(e) => { shape.y1 = num(e); onChange(); }} /></label>
    <label class="field"><span>X2</span><input type="number" step="1" value={shape.x2} onchange={(e) => { shape.x2 = num(e); onChange(); }} /></label>
    <label class="field"><span>Y2</span><input type="number" step="1" value={shape.y2} onchange={(e) => { shape.y2 = num(e); onChange(); }} /></label>
  {:else if shape.kind === 'polygon'}
    <label class="field"><span>Sides</span><input type="number" min="3" max="24" step="1" value={shape.sides} onchange={(e) => { shape.sides = Math.max(3, Math.round(num(e))); onChange(); }} /></label>
    <label class="field"><span>Radius</span><input type="number" step="1" value={shape.radius} onchange={(e) => { shape.radius = num(e); onChange(); }} /></label>
    <label class="field"><span>Rotation</span><input type="number" step="0.01" value={shape.rotation} onchange={(e) => { shape.rotation = num(e); onChange(); }} /></label>
  {:else if shape.kind === 'text'}
    <label class="field"><span>Text</span><input type="text" value={shape.text} onchange={(e) => { shape.text = (e.target as HTMLInputElement).value; onChange(); }} /></label>
    <label class="field"><span>Size</span><input type="number" step="1" value={shape.fontSize} onchange={(e) => { shape.fontSize = num(e); onChange(); }} /></label>
    <label class="field"><span>Tracking</span><input type="number" step="0.5" value={shape.tracking} onchange={(e) => { shape.tracking = num(e); onChange(); }} /></label>
  {:else if shape.kind === 'path'}
    <p class="hint">{shape.points.length} point{shape.points.length === 1 ? '' : 's'} — use the Node tool on the Workspace canvas to move points and add curves.</p>
    <label class="field">
      <span>Closed</span>
      <input type="checkbox" checked={shape.closed} onchange={(e) => { shape.closed = (e.target as HTMLInputElement).checked; onChange(); }} />
    </label>
  {/if}

  {#if shape.kind !== 'line'}
    <div class="sep"></div>
    <label class="field">
      <span>Fill</span>
      <select value={fillSelectValue()} onchange={(e) => setFillMode((e.target as HTMLSelectElement).value)}>
        <option value="custom">Solid color</option>
        <option value="hueA">Hue A (animated)</option>
        <option value="hueB">Hue B (animated)</option>
      </select>
    </label>
    {#if shape.fill.kind === 'flat' && !isSentinel(shape.fill.color)}
      <label class="field"><span>Color</span><input type="color" value={shape.fill.color || '#ffffff'} onchange={(e) => setFillColor((e.target as HTMLInputElement).value)} /></label>
    {:else if shape.fill.kind !== 'flat'}
      <p class="hint">Gradient fill ({shape.fill.kind}, {shape.fill.stops.length} stops) — stop editing isn't supported here yet.</p>
      <button class="small" onclick={flattenGradient}>Flatten to solid color</button>
    {/if}
  {/if}

  <div class="sep"></div>
  <label class="field"><span>Stroke</span><input type="color" value={isSentinel(shape.stroke) ? '#ffffff' : shape.stroke || '#ffffff'} onchange={(e) => { shape.stroke = (e.target as HTMLInputElement).value; onChange(); }} /></label>
  <label class="field"><span>Stroke width</span><input type="number" min="0" step="0.5" value={shape.strokeWidth} onchange={(e) => { shape.strokeWidth = num(e); onChange(); }} /></label>

  <div class="sep"></div>
  <label class="field">
    <span>Glow</span>
    <input type="checkbox" checked={!!shape.glow} onchange={(e) => toggleGlow((e.target as HTMLInputElement).checked)} />
  </label>
  {#if shape.glow}
    <label class="field"><span>Glow blur</span><input type="number" min="0" step="1" value={shape.glow.blur} onchange={(e) => { shape.glow!.blur = num(e); onChange(); }} /></label>
  {/if}
</div>

<style>
  .shapeFields {
    display: flex;
    flex-direction: column;
    gap: 6px;
  }
  .rowHead {
    display: flex;
    align-items: center;
    justify-content: space-between;
  }
  .kindLabel {
    font: 11px var(--mono);
    color: var(--text);
    text-transform: capitalize;
  }
  .field {
    display: flex;
    align-items: center;
    justify-content: space-between;
    gap: 8px;
    font: 11px var(--mono);
    color: var(--dim);
  }
  .field input,
  .field select {
    width: 120px;
  }
  .field input[type='checkbox'] {
    width: auto;
  }
  .hint {
    font: 11px var(--mono);
    color: var(--dim2);
    line-height: 1.4;
  }
  .sep {
    height: 1px;
    background: var(--line);
    margin: 2px 0;
  }
  .danger {
    color: var(--danger);
    border-color: var(--danger);
  }
</style>
