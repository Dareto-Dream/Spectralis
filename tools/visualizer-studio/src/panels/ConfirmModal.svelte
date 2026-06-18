<script lang="ts">
  import { confirmModalState } from '../state/confirmModal.svelte';
  import { focusTrap } from '../lib/focusTrap';

  function respond(value: boolean) {
    confirmModalState.request?.resolve(value);
    confirmModalState.request = null;
  }

  function onKeydown(e: KeyboardEvent) {
    if (e.key === 'Escape') respond(false);
  }
</script>

{#if confirmModalState.request}
  {@const req = confirmModalState.request}
  <!-- svelte-ignore a11y_click_events_have_key_events -->
  <!-- svelte-ignore a11y_no_static_element_interactions -->
  <div class="scrim" onclick={() => respond(false)}>
    <!-- svelte-ignore a11y_no_noninteractive_element_interactions -->
    <div class="modal" role="alertdialog" aria-modal="true" tabindex="-1" onclick={(e) => e.stopPropagation()} onkeydown={onKeydown} use:focusTrap>
      <h3>{req.title}</h3>
      <p>{req.body}</p>
      <div class="actions">
        {#if req.danger}
          <button onclick={() => respond(false)}>Cancel</button>
          <button class="danger" onclick={() => respond(true)}>{req.confirmLabel ?? 'Confirm'}</button>
        {:else}
          <button class="primary" onclick={() => respond(true)}>{req.confirmLabel ?? 'Confirm'}</button>
          <button onclick={() => respond(false)}>Cancel</button>
        {/if}
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
    padding: 16px 18px;
    width: 340px;
    max-width: calc(100vw - 32px);
    box-shadow: 0 12px 40px rgba(0, 0, 0, 0.5);
  }
  .modal h3 {
    margin: 0 0 8px;
    font-size: 14px;
    color: var(--text);
  }
  .modal p {
    margin: 0 0 16px;
    font-size: 12px;
    color: var(--dim);
    line-height: 1.5;
  }
  .actions {
    display: flex;
    justify-content: flex-end;
    gap: 8px;
  }
  .danger {
    background: var(--danger);
    color: #1a0a0a;
  }
  .primary {
    background: var(--accent);
    color: #0a1420;
  }
</style>
