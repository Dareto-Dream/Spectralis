import { describe, expect, it } from 'vitest';
import { LAYER_TEMPLATES } from '../src/lib/templates';

describe('LAYER_TEMPLATES — Assets > Templates > Layers', () => {
  it('has one entry per legacy procedural kind (orb/ring/streak/spin wheel/ambient beam/shard)', () => {
    expect(LAYER_TEMPLATES.map((t) => t.id).sort()).toEqual(
      ['layer-ambient-beam', 'layer-orb', 'layer-ring', 'layer-shard', 'layer-spin-wheel', 'layer-streak'].sort()
    );
  });

  it('every template builds at least one real vector layer with shapes', () => {
    for (const tpl of LAYER_TEMPLATES) {
      const layers = tpl.build(60);
      expect(layers.length).toBeGreaterThan(0);
      for (const layer of layers) {
        expect(layer.type).toBe('vector');
        if (layer.type === 'vector') expect(layer.params.shapes.length).toBeGreaterThan(0);
      }
    }
  });

  it('Spin Wheel bakes a full-song continuous rotation sweep', () => {
    const tpl = LAYER_TEMPLATES.find((t) => t.id === 'layer-spin-wheel')!;
    const [layer] = tpl.build(120);
    expect(layer.tracks.rotation.length).toBeGreaterThan(0);
    expect(layer.tracks.rotation[layer.tracks.rotation.length - 1].t).toBe(120);
  });
});
