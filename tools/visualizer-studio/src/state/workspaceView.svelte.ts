// Pan/zoom/rotate of the WORKSPACE VIEW itself — a viewing convenience, not
// project data. Same category as ProjectStore.timelinePxPerSec: session-only,
// not persisted, not undoable, and orthogonal to any layer's own transform
// (moving the view never touches layer.statics.x/y/etc.).
function clamp(v: number, lo: number, hi: number): number {
  return Math.min(hi, Math.max(lo, v));
}

class WorkspaceView {
  zoom = $state(1);
  panX = $state(0);
  panY = $state(0);
  rotation = $state(0); // radians

  reset() {
    this.zoom = 1;
    this.panX = 0;
    this.panY = 0;
    this.rotation = 0;
  }

  zoomStep(dir: 1 | -1) {
    this.zoom = clamp(this.zoom * (dir > 0 ? 1.2 : 1 / 1.2), 0.1, 8);
  }

  setZoomClamped(z: number) {
    this.zoom = clamp(z, 0.1, 8);
  }
}

export const workspaceView = new WorkspaceView();
