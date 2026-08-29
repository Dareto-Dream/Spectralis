<script lang="ts">
  import { LAYER_TYPE_DEFS } from '../types/layerDefs';
  import type { AnyLayer } from '../types/project';
  import { scrubbable } from '../lib/scrubbableNumber';

  let { layer, onChange }: { layer: AnyLayer; onChange: () => void } = $props();

  const def = $derived(LAYER_TYPE_DEFS[layer.type]);
  const params = $derived(layer.params as Record<string, unknown>);

  function setField(key: string, value: unknown) {
    params[key] = value;
    onChange();
  }

  // Scrub-drag writes live (no commit per pointermove) — one commit fires from
  // onCommit at pointerup, matching the app's "continuous edits commit once" rule.
  function setFieldLive(key: string, value: unknown) {
    params[key] = value;
  }
</script>

<div class="paramFields">
  {#each def.paramFields as field (field.key)}
    <label class="field">
      {#if field.kind === 'number'}
        <span
          class="scrub"
          title="Drag to scrub · Shift = coarse · Alt = fine"
          use:scrubbable={{
            value: Number(params[field.key]) || 0,
            step: field.step ?? 1,
            min: field.min,
            max: field.max,
            onChange: (v: number) => setFieldLive(field.key, v),
            onCommit: onChange,
          }}
        >{field.label}</span>
      {:else}
        <span>{field.label}</span>
      {/if}
      {#if field.kind === 'number'}
        <input
          type="number"
          step={field.step ?? 1}
          min={field.min}
          max={field.max}
          value={params[field.key]}
          onchange={(e) => {
            const v = parseFloat((e.target as HTMLInputElement).value);
            if (!isNaN(v)) setField(field.key, v);
          }}
        />
      {:else if field.kind === 'text'}
        <input
          type="text"
          placeholder={field.placeholder}
          value={params[field.key]}
          onchange={(e) => setField(field.key, (e.target as HTMLInputElement).value)}
        />
      {:else if field.kind === 'checkbox'}
        <input
          type="checkbox"
          checked={Boolean(params[field.key])}
          onchange={(e) => setField(field.key, (e.target as HTMLInputElement).checked)}
        />
      {:else if field.kind === 'select'}
        <select value={params[field.key]} onchange={(e) => setField(field.key, (e.target as HTMLSelectElement).value)}>
          {#each field.options ?? [] as opt (opt)}
            <option value={opt}>{opt}</option>
          {/each}
        </select>
      {/if}
    </label>
  {/each}
  {#if def.hint}<p class="hint">{def.hint}</p>{/if}
</div>

<style>
  .paramFields {
    display: flex;
    flex-direction: column;
    gap: 6px;
  }
  .field {
    display: flex;
    align-items: center;
    justify-content: space-between;
    gap: 8px;
    font: 11px var(--mono);
    color: var(--dim);
  }
  .field input,
  .field select {
    width: 120px;
  }
  .hint {
    font: 11px var(--mono);
    color: var(--dim2);
    line-height: 1.4;
  }
  .scrub {
    cursor: ew-resize;
    user-select: none;
  }
  .scrub.scrubbing,
  .scrub:hover {
    color: var(--accent);
  }
</style>
