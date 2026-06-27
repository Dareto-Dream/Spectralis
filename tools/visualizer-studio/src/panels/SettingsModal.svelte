<script lang="ts">
  import { focusTrap } from '../lib/focusTrap';
  import { themeSettings, THEME_PRESETS, FONT_OPTIONS } from '../state/theme.svelte';

  let { open, onClose }: { open: boolean; onClose: () => void } = $props();

  function onKeydown(e: KeyboardEvent) {
    if (e.key === 'Escape') onClose();
  }

  const scalePct = $derived(Math.round(themeSettings.uiScale * 100));
</script>

{#if open}
  <!-- svelte-ignore a11y_click_events_have_key_events -->
  <!-- svelte-ignore a11y_no_static_element_interactions -->
  <div class="scrim" onclick={onClose}>
    <div class="modal" role="dialog" aria-modal="true" tabindex="-1" onclick={(e) => e.stopPropagation()} onkeydown={onKeydown} use:focusTrap>
      <h3>Settings</h3>

      <section>
        <h4>Theme</h4>
        <div class="swatchRow">
          {#each THEME_PRESETS as p (p.id)}
            <button
              class="swatch"
              class:active={themeSettings.presetId === p.id}
              title={p.label}
              onclick={() => themeSettings.setPreset(p.id)}
              style="background:{p.tokens.bg1}; border-color:{p.tokens.line2};"
            >
              <span class="dot" style="background:{p.tokens.accent}"></span>
              {p.label}
            </button>
          {/each}
        </div>
        <label class="row">
          <span>Custom accent</span>
          <span class="accentControls">
            <input
              type="color"
              value={themeSettings.customAccent ?? themeSettings.preset.tokens.accent}
              oninput={(e) => themeSettings.setCustomAccent((e.target as HTMLInputElement).value)}
            />
            {#if themeSettings.customAccent}
              <button class="small ghost" onclick={() => themeSettings.setCustomAccent(null)}>Reset</button>
            {/if}
          </span>
        </label>
      </section>

      <section>
        <h4>Typography</h4>
        <label class="row">
          <span>Font</span>
          <select value={themeSettings.fontId} onchange={(e) => themeSettings.setFont((e.target as HTMLSelectElement).value)}>
            {#each FONT_OPTIONS as f (f.id)}
              <option value={f.id}>{f.label}</option>
            {/each}
          </select>
        </label>
        <label class="row">
          <span>UI scale ({scalePct}%)</span>
          <input
            type="range"
            min="0.85"
            max="1.3"
            step="0.05"
            value={themeSettings.uiScale}
            oninput={(e) => themeSettings.setUiScale(+(e.target as HTMLInputElement).value)}
          />
        </label>
      </section>

      <div class="actions">
        <button class="small ghost" onclick={() => themeSettings.resetToDefaults()}>Reset to Defaults</button>
        <button class="primary" onclick={onClose}>Close</button>
      </div>
    </div>
  </div>
{/if}

<style>
  .scrim {
    position: fixed;
    inset: 0;
    background: rgba(0, 0, 0, 0.5);
    display: flex;
    align-items: center;
    justify-content: center;
    z-index: 2000;
  }
  .modal {
    background: var(--bg2);
    border: 1px solid var(--line2);
    border-radius: 6px;
    padding: 18px 20px;
    width: 420px;
    max-width: calc(100vw - 32px);
    max-height: calc(100vh - 64px);
    overflow-y: auto;
    box-shadow: 0 12px 40px rgba(0, 0, 0, 0.5);
  }
  .modal h3 {
    margin: 0 0 12px;
    font-size: 14px;
    color: var(--text);
  }
  section {
    margin-bottom: 16px;
  }
  section h4 {
    margin: 0 0 8px;
    font: 10px var(--mono);
    color: var(--dim2);
    text-transform: uppercase;
    letter-spacing: 0.05em;
  }
  .swatchRow {
    display: grid;
    grid-template-columns: 1fr 1fr;
    gap: 6px;
    margin-bottom: 10px;
  }
  .swatch {
    display: flex;
    align-items: center;
    gap: 6px;
    border: 1px solid var(--line);
    border-radius: 4px;
    padding: 8px 10px;
    color: var(--text);
    font: 11px var(--mono);
  }
  .swatch.active {
    outline: 2px solid var(--accent);
    outline-offset: 1px;
  }
  .dot {
    width: 10px;
    height: 10px;
    border-radius: 50%;
    flex-shrink: 0;
  }
  .row {
    display: flex;
    align-items: center;
    justify-content: space-between;
    gap: 10px;
    font: 11px var(--mono);
    color: var(--dim);
    margin-bottom: 8px;
  }
  .row select {
    width: 180px;
  }
  .row input[type='range'] {
    width: 140px;
  }
  .accentControls {
    display: flex;
    align-items: center;
    gap: 6px;
  }
  .accentControls input[type='color'] {
    width: 40px;
    padding: 1px;
  }
  .actions {
    display: flex;
    justify-content: space-between;
    gap: 8px;
    border-top: 1px solid var(--line);
    padding-top: 12px;
  }
</style>
