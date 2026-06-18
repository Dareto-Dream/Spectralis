<script lang="ts">
  import { contextMenuState, closeContextMenu } from '../timeline/contextMenu.svelte';

  let menuEl: HTMLDivElement | undefined = $state();

  let clampedPos = $derived.by(() => {
    const req = contextMenuState.request;
    if (!req) return { x: 0, y: 0 };
    const w = menuEl?.offsetWidth ?? 160;
    const h = menuEl?.offsetHeight ?? 120;
    return {
      x: Math.min(req.x, window.innerWidth - w - 4),
      y: Math.min(req.y, window.innerHeight - h - 4),
    };
  });

  function onWindowClick() {
    closeContextMenu();
  }

  function onKeydown(e: KeyboardEvent) {
    if (e.key === 'Escape') closeContextMenu();
  }

  function run(item: { action: () => void; disabled?: boolean }) {
    if (item.disabled) return;
    item.action();
    closeContextMenu();
  }
</script>

<svelte:window onclick={onWindowClick} onkeydown={onKeydown} />

{#if contextMenuState.request}
  <!-- svelte-ignore a11y_click_events_have_key_events -->
  <!-- svelte-ignore a11y_no_static_element_interactions -->
  <div
    class="menu"
    bind:this={menuEl}
    style="left:{clampedPos.x}px; top:{clampedPos.y}px;"
    onclick={(e) => e.stopPropagation()}
  >
    {#each contextMenuState.request.items as item (item.label)}
      <button class:danger={item.danger} disabled={item.disabled} onclick={() => run(item)}>
        {item.label}
      </button>
    {/each}
  </div>
{/if}

<style>
  .menu {
    position: fixed;
    z-index: 3000;
    background: var(--bg2);
    border: 1px solid var(--line2);
    border-radius: 4px;
    padding: 4px;
    min-width: 140px;
    box-shadow: 0 8px 24px rgba(0, 0, 0, 0.5);
  }
  .menu button {
    display: block;
    width: 100%;
    text-align: left;
    background: none;
    border: none;
    padding: 5px 8px;
    color: var(--text);
    font: 12px var(--mono);
  }
  .menu button:hover:not(:disabled) {
    background: var(--bg4);
  }
  .menu button:disabled {
    color: var(--dim2);
  }
  .menu button.danger {
    color: var(--danger);
  }
</style>
