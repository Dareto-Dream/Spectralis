<script lang="ts">
  // Left-docked tool docker for the Workspace canvas — select/transform,
  // shape tools, the pen tool (full anchor/handle editing), and the
  // paintbrush. Mirrors SvgMaker.svelte's toolbar pattern (a TOOLS array +
  // icon buttons), but targets the layer model instead of local component
  // state — see preview/WorkspaceCanvas.svelte for what each tool actually
  // does on pointer events.
  import { toolState, type ToolId } from '../state/toolState.svelte';
  import MousePointer2 from '@lucide/svelte/icons/mouse-pointer-2';
  import Square from '@lucide/svelte/icons/square';
  import Circle from '@lucide/svelte/icons/circle';
  import Minus from '@lucide/svelte/icons/minus';
  import Pentagon from '@lucide/svelte/icons/pentagon';
  import PenTool from '@lucide/svelte/icons/pen-tool';
  import Paintbrush from '@lucide/svelte/icons/paintbrush';

  const TOOLS: { id: ToolId; icon: typeof MousePointer2; label: string }[] = [
    { id: 'select', icon: MousePointer2, label: 'Select / Transform' },
    { id: 'rect', icon: Square, label: 'Rectangle' },
    { id: 'ellipse', icon: Circle, label: 'Ellipse' },
    { id: 'line', icon: Minus, label: 'Line' },
    { id: 'polygon', icon: Pentagon, label: 'Polygon' },
    { id: 'pen', icon: PenTool, label: 'Pen (anchors)' },
    { id: 'brush', icon: Paintbrush, label: 'Paintbrush (image layers)' },
  ];

  function finishPath() {
    toolState.editingShapeId = null;
    toolState.selectedAnchorId = null;
  }
</script>

<div class="toolsPanel">
  <div class="grid">
    {#each TOOLS as t (t.id)}
      <button class="tool" class:active={toolState.active === t.id} title={t.label} aria-label={t.label} onclick={() => toolState.setTool(t.id)}>
        <t.icon size={16} />
      </button>
    {/each}
  </div>

  <div class="sep"></div>

  {#if toolState.active === 'pen'}
    <div class="options">
      <p class="hint">Click to place anchors, drag while placing to pull a curve handle. Click the start point to close the path.</p>
      {#if toolState.editingShapeId}
        <button class="small" onclick={finishPath}>Done (leave open)</button>
      {/if}
    </div>
  {:else if toolState.active === 'brush'}
    <div class="options">
      <label>
        <span>Size</span>
        <input type="range" min="2" max="120" bind:value={toolState.brushSize} />
      </label>
      <label>
        <span>Opacity</span>
        <input type="range" min="0" max="1" step="0.05" bind:value={toolState.brushOpacity} />
      </label>
      <label>
        <span>Hardness</span>
        <input type="range" min="0" max="1" step="0.05" bind:value={toolState.brushHardness} />
      </label>
      <label>
        <span>Color</span>
        <input type="color" bind:value={toolState.brushColor} />
      </label>
      <p class="hint">Paints onto the selected image layer.</p>
    </div>
  {:else if ['rect', 'ellipse', 'line', 'polygon'].includes(toolState.active)}
    <div class="options">
      <label>
        <span>Fill</span>
        <input type="color" bind:value={toolState.fillColor} />
      </label>
      <label>
        <span>Stroke</span>
        <input type="color" bind:value={toolState.strokeColor} />
      </label>
      <label>
        <span>Stroke width</span>
        <input type="number" min="0" step="0.5" bind:value={toolState.strokeWidth} />
      </label>
      <p class="hint">Drag on the Workspace canvas to draw.</p>
    </div>
  {:else}
    <div class="options">
      <p class="hint">Click a layer to select it. Drag the body to move, corner handles to scale, the top handle to rotate.</p>
    </div>
  {/if}
</div>

<style>
  .toolsPanel {
    display: flex;
    flex-direction: column;
    gap: 8px;
    padding: 6px;
    height: 100%;
  }
  .grid {
    display: grid;
    grid-template-columns: repeat(2, 1fr);
    gap: 4px;
  }
  .tool {
    display: flex;
    align-items: center;
    justify-content: center;
    height: 30px;
    background: var(--bg2);
    border: 1px solid var(--line);
    border-radius: 4px;
    color: var(--dim);
  }
  .tool:hover {
    color: var(--text);
    border-color: var(--line2);
  }
  .tool.active {
    background: var(--accent2);
    color: #1a1400;
    border-color: var(--accent2);
  }
  .sep {
    height: 1px;
    background: var(--line);
  }
  .options {
    display: flex;
    flex-direction: column;
    gap: 8px;
  }
  .options label {
    display: flex;
    align-items: center;
    justify-content: space-between;
    gap: 6px;
    font: 11px var(--mono);
    color: var(--dim);
  }
  .options input[type='range'] {
    width: 110px;
  }
  .options input[type='number'] {
    width: 60px;
  }
  .hint {
    font: 10px var(--mono);
    color: var(--dim2);
    line-height: 1.4;
  }
</style>
