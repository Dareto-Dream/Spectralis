<script lang="ts">
  import type { MenuNode } from '../lib/menuTypes';
  import Check from '@lucide/svelte/icons/check';
  import ChevronRight from '@lucide/svelte/icons/chevron-right';
  import Self from './MenuList.svelte';

  let { items, onAction }: { items: MenuNode[]; onAction: () => void } = $props();

  function run(item: MenuNode) {
    if ((item.kind === 'action' || item.kind === 'checkbox') && !item.disabled) {
      item.action();
      onAction();
    }
  }
</script>

<ul class="menuList">
  {#each items as item, i (i)}
    {#if item.kind === 'separator'}
      <li class="separator" role="separator"></li>
    {:else if item.kind === 'submenu'}
      <li class="hasSub">
        <div class="row" role="menuitem" tabindex="-1">
          <span class="check"></span>
          <span class="label">{item.label}</span>
          <ChevronRight size={12} />
        </div>
        <div class="submenu">
          <Self items={item.items} {onAction} />
        </div>
      </li>
    {:else}
      <li>
        <button class:danger={item.kind === 'action' && item.danger} disabled={item.disabled} onclick={() => run(item)}>
          <span class="check">{#if item.kind === 'checkbox' && item.checked}<Check size={12} />{/if}</span>
          <span class="label">{item.label}</span>
          {#if item.kind === 'action' && item.shortcut}<span class="shortcut">{item.shortcut}</span>{/if}
        </button>
      </li>
    {/if}
  {/each}
</ul>

<style>
  .menuList {
    list-style: none;
    margin: 0;
    padding: 4px;
    min-width: 200px;
  }
  li {
    position: relative;
  }
  li.separator {
    height: 1px;
    background: var(--line);
    margin: 4px 2px;
  }
  button,
  .row {
    display: flex;
    align-items: center;
    gap: 6px;
    width: 100%;
    text-align: left;
    background: none;
    border: none;
    padding: 5px 8px;
    color: var(--text);
    font: 11px var(--mono);
    border-radius: 3px;
    cursor: pointer;
  }
  button:hover:not(:disabled),
  li.hasSub:hover > .row {
    background: var(--bg4);
  }
  button:disabled {
    color: var(--dim2);
    cursor: default;
  }
  button.danger {
    color: var(--danger);
  }
  .check {
    width: 12px;
    flex-shrink: 0;
    display: inline-flex;
    color: var(--accent);
  }
  .label {
    flex: 1;
    white-space: nowrap;
  }
  .shortcut {
    color: var(--dim2);
    font-size: 10px;
    margin-left: 12px;
  }
  .submenu {
    display: none;
    position: absolute;
    left: 100%;
    top: -4px;
    background: var(--bg2);
    border: 1px solid var(--line2);
    border-radius: 4px;
    box-shadow: 0 8px 24px rgba(0, 0, 0, 0.5);
    z-index: 1;
  }
  li.hasSub:hover > .submenu {
    display: block;
  }
</style>
