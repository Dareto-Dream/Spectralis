// The Workspace canvas's active tool + its option defaults — module singleton
// shared between ToolsPanel.svelte (tool picker + option controls) and
// preview/WorkspaceCanvas.svelte (the pointer handlers that actually act on
// whichever tool is active), same pattern as dockManager/toast.
export type ToolId = 'select' | 'rect' | 'ellipse' | 'line' | 'polygon' | 'pen' | 'brush';

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

  // Pen-tool authoring state — which path shape (by id, within the target
  // layer) is currently being drawn/edited, and which anchor is selected
  // (for Delete-to-remove and for showing its handles).
  editingShapeId: string | null = $state(null);
  selectedAnchorId: string | null = $state(null);

  setTool(id: ToolId) {
    this.active = id;
    if (id !== 'pen') {
      this.editingShapeId = null;
      this.selectedAnchorId = null;
    }
  }
}

export const toolState = new ToolState();
