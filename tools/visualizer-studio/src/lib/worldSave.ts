// Save/Load/Export for World mode's node graph — mirrors projectSave.ts's
// shape for Capsule, but against .spectral (SPWX/door) instead of .spectralis.
// No undo/history and no createdAt preservation across saves yet, matching
// NodeWorldStore's own documented gaps for this pass.
import { nodeWorldStore } from '../state/nodeWorld.svelte';
import type { SceneNode } from '../types/node';
import { encodeWorldFile, decodeWorldFile, newWorldMeta, type WorldFile } from '../format/worldFile';
import { buildNodeGraphHtml } from '../world-tab/buildNodeGraphHtml';
import { downloadBytes, downloadText } from './downloadText';
import { toast } from '../state/toast.svelte';
import { worldAutosave } from '../state/autosave.svelte';

function slug(name: string): string {
  return (name || 'world').toLowerCase().replace(/[^a-z0-9\-_]/gi, '-');
}

// Shared with state/autosave.svelte.ts's world autosave — same reasoning as
// projectSave.ts's buildCapsuleFile.
export function buildWorldFile(): WorldFile {
  return {
    meta: newWorldMeta({ name: nodeWorldStore.meta.name, author: nodeWorldStore.meta.author }),
    tracks: [],
    layout: [],
    nodeGraph: $state.snapshot(nodeWorldStore.roots),
    passthrough: [],
  };
}

// Shared core for Save (Ctrl+S) and Save As (Ctrl+Shift+S) — same reasoning
// as projectSave.ts's writeProjectFile.
async function writeWorldFile(forcePrompt: boolean) {
  const bytes = encodeWorldFile(buildWorldFile());
  const name = slug(nodeWorldStore.meta.name);
  if (window.native) {
    let path = forcePrompt ? null : nodeWorldStore.knownFilePath;
    if (!path) {
      const root = await window.native.getStudioRoot();
      const chosen = await window.native.saveFileDialog({
        defaultPath: nodeWorldStore.knownFilePath ?? `${root}/Projects/${name}.spectral`,
        filters: [{ name: 'Spectralis World', extensions: ['spectral'] }],
      });
      if (!chosen) return;
      path = chosen;
    }
    await window.native.writeBinaryFile(path, bytes.buffer.slice(bytes.byteOffset, bytes.byteOffset + bytes.byteLength) as ArrayBuffer);
    nodeWorldStore.markSaved(path);
  } else {
    downloadBytes(`${name}.spectral`, bytes);
    nodeWorldStore.markSaved(null);
  }
  void worldAutosave.clear();
  toast.push('success', 'World saved');
}

export function saveWorldFile() {
  return writeWorldFile(false);
}

export function saveWorldFileAs() {
  return writeWorldFile(true);
}

export async function loadWorldFile(file: File) {
  try {
    const bytes = new Uint8Array(await file.arrayBuffer());
    const wf = decodeWorldFile(bytes);
    const nodeGraph = Array.isArray(wf.nodeGraph) ? (wf.nodeGraph as SceneNode[]) : [];
    // Native only — see projectImport.ts's importProjectFile for the same trick.
    const knownPath = window.native?.getPathForFile(file) ?? null;
    nodeWorldStore.loadGraph(nodeGraph, { name: wf.meta.name, author: wf.meta.author }, knownPath);
    toast.push('success', `Loaded ${file.name}`);
  } catch (err) {
    toast.push('error', `Couldn't load that world — ${err instanceof Error ? err.message : String(err)}`);
  }
}

export async function exportWorldHtml() {
  const html = buildNodeGraphHtml(nodeWorldStore.meta, $state.snapshot(nodeWorldStore.roots));
  const name = `${slug(nodeWorldStore.meta.name)}_world.html`;
  if (window.native) {
    const root = await window.native.getStudioRoot();
    const chosen = await window.native.saveFileDialog({
      defaultPath: `${root}/Projects/${name}`,
      filters: [{ name: 'HTML', extensions: ['html'] }],
    });
    if (!chosen) return;
    await window.native.writeBinaryFile(chosen, new TextEncoder().encode(html).buffer as ArrayBuffer);
  } else {
    downloadText(name, html);
  }
  toast.push('success', 'World exported');
}
