import { describe, expect, it } from 'vitest';
import { WorldStore } from '../src/state/world.svelte';
import { StoryStore } from '../src/state/story.svelte';
import { AssetLibrary } from '../src/state/assetLibrary.svelte';
import { NodeWorldStore } from '../src/state/nodeWorld.svelte';
import { serializeSvgDocument, type Shape } from '../src/lib/svgShapes';
import { runScript } from '../src/lib/scriptRun';
import { buildNodeElement, runNodeScript, applyNodeTransform } from '../src/core/nodeRender.js';

describe('WorldStore', () => {
  it('addTrack auto-numbers id/title/audio/cover from the current track count', () => {
    const w = new WorldStore();
    w.tracks = [];
    const t = w.addTrack();
    expect(t.id).toBe('track-01');
    expect(t.audio).toBe('tracks/01/audio.mp3');
  });

  it('addTrack(partial) lets a caller (e.g. the script console) override any field', () => {
    const w = new WorldStore();
    w.tracks = [];
    const t = w.addTrack({ id: 'custom-id', title: 'Custom' });
    expect(t.id).toBe('custom-id');
    expect(t.title).toBe('Custom');
  });

  it('removeTrack drops exactly the row at that index', () => {
    const w = new WorldStore();
    w.tracks = [];
    w.addTrack({ id: 'a' });
    w.addTrack({ id: 'b' });
    w.removeTrack(0);
    expect(w.tracks.map((t) => t.id)).toEqual(['b']);
  });

  it('reorderTrack moves a track to a new index without dropping any', () => {
    const w = new WorldStore();
    w.tracks = [];
    w.addTrack({ id: 'a' });
    w.addTrack({ id: 'b' });
    w.addTrack({ id: 'c' });
    w.reorderTrack(0, 2);
    expect(w.tracks.map((t) => t.id)).toEqual(['b', 'c', 'a']);
  });

  it('reorderTrack no-ops on an out-of-range index instead of throwing', () => {
    const w = new WorldStore();
    w.tracks = [];
    w.addTrack({ id: 'a' });
    expect(() => w.reorderTrack(0, 5)).not.toThrow();
    expect(w.tracks.map((t) => t.id)).toEqual(['a']);
  });
});

describe('StoryStore', () => {
  it('addPage gives every page a stable id even with no partial given', () => {
    const s = new StoryStore();
    s.pages = [];
    const p1 = s.addPage();
    const p2 = s.addPage();
    expect(p1.id).not.toBe(p2.id);
  });

  it('reorderPage moves a page to a new index without dropping any', () => {
    const s = new StoryStore();
    s.pages = [];
    s.addPage({ id: 'a', text: 'A' });
    s.addPage({ id: 'b', text: 'B' });
    s.reorderPage(1, 0);
    expect(s.pages.map((p) => p.id)).toEqual(['b', 'a']);
  });
});

describe('AssetLibrary', () => {
  it('addSvg stores a base64 data URL and marks the kind as svg', () => {
    const lib = new AssetLibrary();
    const entry = lib.addSvg('icon', '<svg></svg>');
    expect(entry.kind).toBe('svg');
    expect(entry.dataUrl.startsWith('data:image/svg+xml;base64,')).toBe(true);
    expect(entry.name).toBe('icon.svg');
  });

  it('addSvg does not double the .svg extension if the name already has one', () => {
    const lib = new AssetLibrary();
    const entry = lib.addSvg('icon.svg', '<svg></svg>');
    expect(entry.name).toBe('icon.svg');
  });

  it('remove drops exactly the matching entry', () => {
    const lib = new AssetLibrary();
    const a = lib.addSvg('a', '<svg/>');
    const b = lib.addSvg('b', '<svg/>');
    lib.remove(a.id);
    expect(lib.assets.map((e) => e.id)).toEqual([b.id]);
  });

  it('rename ignores a blank name instead of clearing the asset name', () => {
    const lib = new AssetLibrary();
    const a = lib.addSvg('original', '<svg/>');
    lib.rename(a.id, '   ');
    expect(lib.get(a.id)?.name).toBe('original.svg');
  });
});

describe('svgShapes.serializeSvgDocument', () => {
  it('wraps shapes in a standalone <svg> with the given viewBox', () => {
    const shapes: Shape[] = [{ id: '1', type: 'rect', x: 0, y: 0, w: 10, h: 10, fill: '#fff', stroke: '#000', strokeWidth: 1 }];
    const svg = serializeSvgDocument(shapes, 100, 50);
    expect(svg).toContain('viewBox="0 0 100 50"');
    expect(svg).toContain('<rect');
  });

  it('a path shape always serializes fill="none" regardless of its fill field — it has no fillable interior', () => {
    const shapes: Shape[] = [{ id: '1', type: 'path', d: 'M 0 0 L 1 1', fill: '#fff', stroke: '#000', strokeWidth: 1 }];
    const svg = serializeSvgDocument(shapes, 10, 10);
    expect(svg).toContain('fill="none"');
  });
});

describe('scriptRun.runScript', () => {
  it('captures console.log output from the script', () => {
    const result = runScript('console.log("hi", 42)', {});
    expect(result.ok).toBe(true);
    expect(result.logs).toEqual(['hi 42']);
  });

  it('exposes ctx entries as named arguments the script can call', () => {
    const calls: number[] = [];
    const result = runScript('ctx.bump(1); ctx.bump(2);', { ctx: { bump: (n: number) => calls.push(n) } });
    expect(result.ok).toBe(true);
    expect(calls).toEqual([1, 2]);
  });

  it('a thrown error is reported, not left to propagate to the caller', () => {
    const result = runScript('throw new Error("boom")', {});
    expect(result.ok).toBe(false);
    expect(result.error).toContain('boom');
  });
});

describe('NodeWorldStore', () => {
  it('addNode(null) creates a root node and selects it', () => {
    const w = new NodeWorldStore();
    const node = w.addNode(null);
    expect(w.roots).toEqual([node]);
    expect(w.selectedId).toBe(node.id);
  });

  it('addNode(parentId) nests the new node inside the parent, not the roots array', () => {
    const w = new NodeWorldStore();
    const parent = w.addNode(null);
    const child = w.addNode(parent.id);
    expect(w.roots).toEqual([parent]);
    expect(parent.children).toEqual([child]);
  });

  it('deleteNode removes exactly that node, wherever it lives in the tree', () => {
    const w = new NodeWorldStore();
    const parent = w.addNode(null);
    const child = w.addNode(parent.id);
    w.deleteNode(child.id);
    expect(parent.children).toEqual([]);
    expect(w.findNode(child.id)).toBeNull();
  });

  it('duplicateNode clones with fresh ids (including nested children) and inserts right after the original', () => {
    const w = new NodeWorldStore();
    const parent = w.addNode(null);
    const child = w.addNode(parent.id);
    child.assetIds.push('asset-1');
    const clone = w.duplicateNode(parent.id)!;
    expect(w.roots.map((n) => n.id)).toEqual([parent.id, clone.id]);
    expect(clone.id).not.toBe(parent.id);
    expect(clone.children[0].id).not.toBe(child.id);
    expect(clone.children[0].assetIds).toEqual(['asset-1']);
  });

  it('toggleAsset/toggleScript add on first call and remove on second', () => {
    const w = new NodeWorldStore();
    const node = w.addNode(null);
    w.toggleAsset(node.id, 'img-1');
    expect(node.assetIds).toEqual(['img-1']);
    w.toggleAsset(node.id, 'img-1');
    expect(node.assetIds).toEqual([]);
  });

  it('updateTransform patches only the given fields', () => {
    const w = new NodeWorldStore();
    const node = w.addNode(null);
    w.updateTransform(node.id, { x: 42 });
    expect(node.x).toBe(42);
    expect(node.y).toBe(0);
  });

  it('isDirty flips true on any mutation and back to false once markSaved catches up', () => {
    const w = new NodeWorldStore();
    expect(w.isDirty).toBe(false);
    w.addNode(null);
    expect(w.isDirty).toBe(true);
    w.markSaved('C:/Projects/world.spectral');
    expect(w.isDirty).toBe(false);
    expect(w.knownFilePath).toBe('C:/Projects/world.spectral');
  });

  it('a mutation after markSaved makes it dirty again', () => {
    const w = new NodeWorldStore();
    w.markSaved(null);
    w.addNode(null);
    expect(w.isDirty).toBe(true);
  });

  it('loadGraph treats the freshly loaded graph as clean (not dirty) and records its known path', () => {
    const w = new NodeWorldStore();
    w.addNode(null); // dirty from some prior session state
    w.loadGraph([], { name: 'Loaded' }, 'C:/Projects/world.spectral');
    expect(w.isDirty).toBe(false);
    expect(w.knownFilePath).toBe('C:/Projects/world.spectral');
  });
});

describe('core/nodeRender.js', () => {
  it('buildNodeElement renders the node id, label text, and nested children as real DOM', () => {
    const tree = {
      id: 'n1',
      name: 'Parent',
      x: 0,
      y: 0,
      rotation: 0,
      scale: 1,
      assetIds: [],
      scriptIds: [],
      spritesheet: null,
      children: [{ id: 'n2', name: 'Child', x: 5, y: 5, rotation: 0, scale: 1, assetIds: [], scriptIds: [], spritesheet: null, children: [] }],
    };
    const el = buildNodeElement(tree, () => null, () => null, document);
    expect(el.dataset.nodeId).toBe('n1');
    expect(el.querySelector('.sp-node-label')?.textContent).toBe('Parent');
    const childEl = el.querySelector('[data-node-id="n2"]');
    expect(childEl?.querySelector('.sp-node-label')?.textContent).toBe('Child');
  });

  it("a node with an attached script gets its hover handler wired and reacts to mouseenter/mouseleave", () => {
    const node = { id: 'n1', name: 'Hoverable', x: 0, y: 0, rotation: 0, scale: 1, assetIds: [], scriptIds: ['script-1'], spritesheet: null, children: [] };
    const script = "on('hover', (node) => { node.scale = 2; }); on('unhover', (node) => { node.scale = 1; });";
    const el = buildNodeElement(node, () => null, (id) => (id === 'script-1' ? script : null), document);
    expect(node.scale).toBe(1);
    el.dispatchEvent(new Event('mouseenter'));
    expect(node.scale).toBe(2);
    el.dispatchEvent(new Event('mouseleave'));
    expect(node.scale).toBe(1);
  });

  it('runNodeScript executes code with exactly `node` and `on` bound', () => {
    const calls: [string, () => number][] = [];
    const on = (event: string, handler: () => number) => calls.push([event, handler]);
    runNodeScript('on("click", () => node.scale);', { scale: 5 }, on);
    expect(calls).toHaveLength(1);
    expect(calls[0][0]).toBe('click');
    expect(calls[0][1]()).toBe(5);
  });

  it('applyNodeTransform writes x/y/rotation/scale into a CSS transform string', () => {
    const el = document.createElement('div');
    applyNodeTransform(el, { x: 10, y: 20, rotation: 45, scale: 2 });
    expect(el.style.transform).toContain('translate(10px, 20px)');
    expect(el.style.transform).toContain('rotate(45deg)');
    expect(el.style.transform).toContain('scale(2)');
  });
});
