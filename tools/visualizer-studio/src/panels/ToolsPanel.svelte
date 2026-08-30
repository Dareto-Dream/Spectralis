<script lang="ts">
  // Left-docked tool docker for the Workspace canvas — select/transform,
  // shape tools, the pen tool (draw new anchors/curves), the node tool (edit
  // existing anchors/curves), and the paintbrush. Purely icon buttons with a
  // native hover tooltip (the `title` attr) each — no per-tool option
  // controls live here; those are in ActionBar.svelte's top bar instead, so
  // this docker never grows/shrinks or wraps text as you switch tools.
  import { toolState, type ToolId } from '../state/toolState.svelte';
  import MousePointer2 from '@lucide/svelte/icons/mouse-pointer-2';
  import Square from '@lucide/svelte/icons/square';
  import Circle from '@lucide/svelte/icons/circle';
  import Minus from '@lucide/svelte/icons/minus';
  import Pentagon from '@lucide/svelte/icons/pentagon';
  import PenTool from '@lucide/svelte/icons/pen-tool';
  import SplinePointer from '@lucide/svelte/icons/spline-pointer';
  import Paintbrush from '@lucide/svelte/icons/paintbrush';

  const TOOLS: { id: ToolId; icon: typeof MousePointer2; label: string }[] = [
    { id: 'select', icon: MousePointer2, label: 'Select / Transform' },
    { id: 'rect', icon: Square, label: 'Rectangle' },
    { id: 'ellipse', icon: Circle, label: 'Ellipse' },
    { id: 'line', icon: Minus, label: 'Line' },
    { id: 'polygon', icon: Pentagon, label: 'Polygon' },
    { id: 'pen', icon: PenTool, label: 'Pen — draw new anchors/curves' },
    { id: 'node', icon: SplinePointer, label: 'Node — move points, add curves' },
    { id: 'brush', icon: Paintbrush, label: 'Paintbrush (image layers)' },
  ];
</script>

<div class="toolsPanel">
  <div class="grid">
    {#each TOOLS as t (t.id)}
      <button class="tool" class:active={toolState.active === t.id} title={t.label} aria-label={t.label} onclick={() => toolState.setTool(t.id)}>
        <t.icon size={16} />
      </button>
    {/each}
  </div>
</div>

<style>
  .toolsPanel {
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
</style>
