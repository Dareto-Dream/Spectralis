<script lang="ts">
  import { focusTrap } from '../lib/focusTrap';

  let { open, onClose }: { open: boolean; onClose: () => void } = $props();

  const SHORTCUTS: [string, string][] = [
    ['Space', 'Play / Pause'],
    ['Ctrl+Z / Ctrl+Shift+Z', 'Undo / Redo'],
    ['Delete / Backspace', 'Delete selected keyframe(s) or layer'],
    ['Ctrl+C / Ctrl+V', 'Copy / Paste keyframes at playhead'],
    ['Ctrl+Shift+V', 'Paste keyframes at original times'],
    ['Ctrl+A', 'Select all keyframes in the active track'],
    ['Ctrl+D', 'Duplicate selected layer'],
    ['F2', 'Rename selected layer'],
    ['Escape', 'Clear selection'],
    ['← / →', 'Nudge playhead (or selected keyframes) by 1 frame'],
    ['Shift+← / Shift+→', 'Nudge by 1 second'],
    ['↑ / ↓', 'Move layer selection up/down the list'],
    ['[ / ]', 'Set selected section start/end to playhead'],
    ['+ / -', 'Timeline zoom in/out'],
    ['Ctrl+S', 'Save project'],
    ['Double-click a lane', 'Add a keyframe at that time'],
    ['Scroll wheel over timeline', 'Zoom, anchored at cursor'],
    ['Right-click keyframe/section/layer', 'Context menu'],
    ['?', 'Show this help'],
  ];

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
      <div class="shortcuts">
        {#each SHORTCUTS as [key, desc] (key)}
          <span class="key">{key}</span>
          <span class="desc">{desc}</span>
        {/each}
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
  .shortcuts {
    display: grid;
    grid-template-columns: max-content 1fr;
    column-gap: 18px;
    row-gap: 6px;
    margin-bottom: 16px;
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
