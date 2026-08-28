<script lang="ts">
  import { focusTrap } from '../lib/focusTrap';
  import { SHORTCUTS, type ShortcutCategory } from '../lib/keymap';
  import Search from '@lucide/svelte/icons/search';

  let { open, onClose }: { open: boolean; onClose: () => void } = $props();

  const CATEGORIES: ShortcutCategory[] = ['Playback', 'Editing', 'Selection', 'View', 'General'];

  let query = $state('');
  let activeCategory: ShortcutCategory | 'all' = $state('all');

  const filtered = $derived(
    SHORTCUTS.filter(
      (s) =>
        (activeCategory === 'all' || s.category === activeCategory) &&
        (s.keys.toLowerCase().includes(query.toLowerCase()) || s.description.toLowerCase().includes(query.toLowerCase()))
    )
  );
  const grouped = $derived(
    CATEGORIES.map((cat) => ({ cat, items: filtered.filter((s) => s.category === cat) })).filter((g) => g.items.length)
  );

  function onKeydown(e: KeyboardEvent) {
    if (e.key === 'Escape') onClose();
  }
</script>

{#if open}
  <!-- svelte-ignore a11y_click_events_have_key_events -->
  <!-- svelte-ignore a11y_no_static_element_interactions -->
  <div class="scrim" onclick={onClose}>
    <div class="modal" role="dialog" aria-modal="true" tabindex="-1" onclick={(e) => e.stopPropagation()} onkeydown={onKeydown} use:focusTrap>
      <h3>Keyboard Shortcuts</h3>

      <div class="toolbar">
        <div class="catFilter">
          <button class="small ghost" class:active={activeCategory === 'all'} onclick={() => (activeCategory = 'all')}>All</button>
          {#each CATEGORIES as cat (cat)}
            <button class="small ghost" class:active={activeCategory === cat} onclick={() => (activeCategory = cat)}>{cat}</button>
          {/each}
        </div>
        <div class="search">
          <Search size={12} />
          <input type="text" placeholder="filter…" bind:value={query} />
        </div>
      </div>

      <div class="groups">
        {#if !grouped.length}
          <p class="empty">Nothing matches.</p>
        {:else}
          {#each grouped as g (g.cat)}
            <section>
              <h4>{g.cat}</h4>
              <div class="shortcuts">
                {#each g.items as s (s.keys + s.description)}
                  <span class="key">{s.keys}</span>
                  <span class="desc">{s.description}</span>
                {/each}
              </div>
            </section>
          {/each}
        {/if}
      </div>

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
    width: 560px;
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
  .toolbar {
    display: flex;
    align-items: center;
    gap: 8px;
    margin-bottom: 14px;
    flex-wrap: wrap;
  }
  .catFilter {
    display: flex;
    gap: 2px;
    flex-wrap: wrap;
  }
  .catFilter button.active {
    background: var(--bg3);
    color: var(--accent);
  }
  .search {
    display: flex;
    align-items: center;
    gap: 4px;
    color: var(--dim2);
    margin-left: auto;
  }
  .search input {
    width: 140px;
  }
  .groups {
    margin-bottom: 16px;
  }
  section {
    margin-bottom: 14px;
  }
  section h4 {
    margin: 0 0 8px;
    font: 10px var(--mono);
    color: var(--dim2);
    text-transform: uppercase;
    letter-spacing: 0.05em;
  }
  .empty {
    padding: 20px;
    text-align: center;
    font: 11px var(--mono);
    color: var(--dim2);
  }
  .shortcuts {
    display: grid;
    grid-template-columns: max-content 1fr;
    column-gap: 18px;
    row-gap: 6px;
    align-items: baseline;
  }
  .key {
    font: 11px var(--mono);
    color: var(--accent2);
    white-space: nowrap;
  }
  .desc {
    font: 11px var(--mono);
    color: var(--dim);
    line-height: 1.4;
  }
  .primary {
    width: 100%;
  }
</style>
