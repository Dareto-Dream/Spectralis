<script lang="ts">
  import { focusTrap } from '../lib/focusTrap';

  let { open, onClose }: { open: boolean; onClose: () => void } = $props();

  function onKeydown(e: KeyboardEvent) {
    if (e.key === 'Escape') onClose();
  }
</script>

{#if open}
  <!-- svelte-ignore a11y_click_events_have_key_events -->
  <!-- svelte-ignore a11y_no_static_element_interactions -->
  <div class="scrim" onclick={onClose}>
    <div class="modal" role="dialog" aria-modal="true" tabindex="-1" onclick={(e) => e.stopPropagation()} onkeydown={onKeydown} use:focusTrap>
      <h3>Visualizer Studio</h3>
      <p>
        An AE-style layer/keyframe/curve editor for Spectralis capsule audio visualizers, plus the World and Story
        capsule builders — all as dockable panels in one workspace.
      </p>
      <p class="dim">Runs standalone in a browser, or as a packaged desktop app — no account, no network calls.</p>
      <button class="primary" onclick={onClose}>Close</button>
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
    width: 360px;
    max-width: calc(100vw - 32px);
    box-shadow: 0 12px 40px rgba(0, 0, 0, 0.5);
  }
  .modal h3 {
    margin: 0 0 10px;
    font-size: 14px;
    color: var(--text);
  }
  .modal p {
    margin: 0 0 12px;
    font: 11px var(--mono);
    color: var(--dim);
    line-height: 1.5;
  }
  .modal p.dim {
    color: var(--dim2);
  }
  .modal .primary {
    width: 100%;
  }
</style>
