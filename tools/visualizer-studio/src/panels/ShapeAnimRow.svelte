<script lang="ts">
  // Per-shape counterpart to PropRow.svelte — same stopwatch-to-enable-
  // keyframing pattern, scoped to one shape's animTracks/animStatics (see
  // types/project.ts's ShapeAnimKey doc) instead of a layer's tracks/statics.
  // Shown in ShapeFields.svelte's "Motion" section for the currently
  // selected shape.
  import type { AnyLayer, ShapeAnimKey, VectorShape } from '../types/project';
  import { SHAPE_ANIM_DEFAULTS } from '../types/project';
  import type { ProjectStore } from '../state/project.svelte';
  import { scrubbable } from '../lib/scrubbableNumber';
  import Timer from '@lucide/svelte/icons/timer';

  let { store, layer, shape, animKey, label }: { store: ProjectStore; layer: AnyLayer; shape: VectorShape; animKey: ShapeAnimKey; label: string } = $props();

  const track = $derived(shape.animTracks?.[animKey] ?? []);
  const keyframed = $derived(track.length > 0);
  const staticValue = $derived(shape.animStatics?.[animKey] ?? SHAPE_ANIM_DEFAULTS[animKey]);

  function onStaticChange(e: Event) {
    const v = parseFloat((e.target as HTMLInputElement).value);
    if (isNaN(v)) return;
    shape.animStatics = { ...(shape.animStatics ?? {}), [animKey]: v };
    store.commit();
  }
</script>

<div class="propRow">
  <button
    class="kbtn"
    class:on={keyframed}
    title={keyframed ? 'Keyframed — click to disable' : 'Enable keyframing'}
    aria-label={keyframed ? `${label}: keyframed, click to disable` : `Enable keyframing for ${label}`}
    onclick={() => store.toggleShapeKeyframing(layer.id, shape.id, animKey)}
  >
    <Timer size={12} />
  </button>
  {#if keyframed}
    <span class="label">{label}</span>
  {:else}
    <span
      class="label scrub"
      title="Drag to scrub value"
      use:scrubbable={{
        value: staticValue,
        step: animKey === 'scale' ? 0.05 : 0.5,
        onChange: (v: number) => (shape.animStatics = { ...(shape.animStatics ?? {}), [animKey]: v }),
        onCommit: () => store.commit(),
      }}
    >
      {label}
    </span>
  {/if}
  {#if keyframed}
    <span class="kfCount">{track.length} keys</span>
    <button class="small" title="Add key at playhead" onclick={() => store.addShapeKeyframeAtPlayhead(layer.id, shape.id, animKey)}>+Key</button>
  {:else}
    <input type="number" step={animKey === 'scale' ? 0.05 : 0.5} value={staticValue} onchange={onStaticChange} />
  {/if}
</div>

<style>
  .propRow {
    display: flex;
    align-items: center;
    gap: 6px;
    padding: 2px 4px;
    border-radius: 3px;
  }
  .kbtn {
    background: none;
    border: 1px solid var(--line);
    border-radius: 3px;
    color: var(--dim);
    width: 20px;
    height: 20px;
    padding: 0;
    justify-content: center;
  }
  .kbtn.on {
    background: var(--accent2);
    color: #1a1400;
    border-color: var(--accent2);
  }
  .label {
    flex: 1;
    font: 11px var(--mono);
    color: var(--text);
  }
  .label.scrub {
    cursor: ew-resize;
  }
  .label.scrub:hover {
    color: var(--accent);
  }
  .kfCount {
    font: 10px var(--mono);
    color: var(--accent);
  }
  input[type='number'] {
    width: 70px;
  }
</style>
