// Generic Svelte action for drag-and-drop file intake (plan QoL §C). The old
// tool has zero drop handlers anywhere — every drop target added on top of it
// (audio lane, LRC import, cover image, project load) shares this one action
// so the "live" `.dragOver` outline state and the reject-with-a-toast-not-a-
// silent-no-op behavior are implemented exactly once.
export interface DropZoneOptions {
  accept: (file: File) => boolean;
  onDrop: (file: File) => void;
  onReject?: (file: File) => void;
}

function hasFiles(e: DragEvent): boolean {
  return Array.from(e.dataTransfer?.types ?? []).includes('Files');
}

export function dropZone(node: HTMLElement, opts: DropZoneOptions) {
  let current = opts;
  let depth = 0;

  function onDragEnter(e: DragEvent) {
    if (!hasFiles(e)) return;
    e.preventDefault();
    depth++;
    node.classList.add('dragOver');
  }
  function onDragOver(e: DragEvent) {
    if (!hasFiles(e)) return;
    e.preventDefault();
  }
  function onDragLeave() {
    depth = Math.max(0, depth - 1);
    if (depth === 0) node.classList.remove('dragOver');
  }
  function onDrop(e: DragEvent) {
    if (!hasFiles(e)) return;
    e.preventDefault();
    depth = 0;
    node.classList.remove('dragOver');
    const file = e.dataTransfer?.files?.[0];
    if (!file) return;
    if (current.accept(file)) current.onDrop(file);
    else current.onReject?.(file);
  }

  node.addEventListener('dragenter', onDragEnter);
  node.addEventListener('dragover', onDragOver);
  node.addEventListener('dragleave', onDragLeave);
  node.addEventListener('drop', onDrop);

  return {
    update(newOpts: DropZoneOptions) {
      current = newOpts;
    },
    destroy() {
      node.removeEventListener('dragenter', onDragEnter);
      node.removeEventListener('dragover', onDragOver);
      node.removeEventListener('dragleave', onDragLeave);
      node.removeEventListener('drop', onDrop);
    },
  };
}
