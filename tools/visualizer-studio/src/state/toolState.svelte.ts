// The Workspace canvas's active tool + its option defaults — module singleton
// shared between ToolsPanel.svelte (the icon-only tool picker), ActionBar.svelte
// (the tool's option controls, which live in the top bar, not the tool
// docker), and preview/WorkspaceCanvas.svelte (the pointer handlers that
// actually act on whichever tool is active), same pattern as dockManager/toast.
export type ToolId = 'select' | 'rect' | 'ellipse' | 'line' | 'polygon' | 'pen' | 'node' | 'brush';

class ToolState {
  active: ToolId = $state('select');

  // New-shape defaults (shape tools read these when creating a shape).
  fillColor = $state('#5ec8ff');
  strokeColor = $state('#ffffff');
  strokeWidth = $state(2);

  // Paintbrush.
  brushSize = $state(24);
  brushColor = $state('#ffffff');
  brushOpacity = $state(1);
  brushHardness = $state(1); // 0 = soft (blurred edge), 1 = hard

  // Pen/Node authoring state — which path shape (by id, within the target
  // layer) is currently being drawn/edited, and which anchor is selected
  // (for Delete-to-remove and for showing its handles). Shared by both tools
  // so switching Pen <-> Node mid-edit keeps working on the same path.
  editingShapeId: string | null = $state(null);
  selectedAnchorId: string | null = $state(null);

  // Select tool: the individual shape a click landed on, within the selected
  // vector layer — clicking a shape only ever targets THAT shape (see
  // WorkspaceCanvas.svelte's pickShapeAt), never "the whole layer" the way a
  // single flat bounding box used to. Read by InspectorPanel.svelte to show
  // ShapeFields for it.
  selectedShapeId: string | null = $state(null);

  setTool(id: ToolId) {
    this.active = id;
    if (id !== 'pen' && id !== 'node') {
      this.editingShapeId = null;
      this.selectedAnchorId = null;
    }
    if (id !== 'select') this.selectedShapeId = null;
  }

  finishPath() {
    this.editingShapeId = null;
    this.selectedAnchorId = null;
  }
}

export const toolState = new ToolState();
