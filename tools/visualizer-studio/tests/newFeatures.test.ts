import { describe, expect, it } from 'vitest';
import { WorldStore } from '../src/state/world.svelte';
import { StoryStore } from '../src/state/story.svelte';
import { AssetLibrary } from '../src/state/assetLibrary.svelte';
import { serializeSvgDocument, type Shape } from '../src/lib/svgShapes';
import { runScript } from '../src/lib/scriptRun';

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
