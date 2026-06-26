<script lang="ts">
  import { toast } from '../state/toast.svelte';
  import X from '@lucide/svelte/icons/x';
</script>

<div class="toastStack">
  {#each toast.toasts as t (t.id)}
    <div class="toast {t.kind}">
      <span>{t.text}</span>
      <button class="icon ghost" onclick={() => toast.dismiss(t.id)} aria-label="Dismiss"><X size={12} /></button>
    </div>
  {/each}
</div>

<style>
  .toastStack {
    position: fixed;
    right: 16px;
    bottom: 16px;
    display: flex;
    flex-direction: column;
    gap: 8px;
    z-index: 1000;
  }
  .toast {
    display: flex;
    align-items: center;
    gap: 10px;
    background: var(--bg3);
    border: 1px solid var(--line);
    border-left: 3px solid var(--accent);
    border-radius: 4px;
    padding: 8px 10px;
    font: 12px var(--mono);
    color: var(--text);
    min-width: 220px;
    box-shadow: 0 4px 16px rgba(0, 0, 0, 0.4);
  }
  .toast.success {
    border-left-color: var(--good);
  }
  .toast.error {
    border-left-color: var(--danger);
  }
  .toast button {
    margin-left: auto;
    background: none;
    border: none;
    color: var(--dim);
    cursor: pointer;
    padding: 0 2px;
  }
</style>
