<script lang="ts">
  import { focusTrap } from '../lib/focusTrap';

  let { open, onClose }: { open: boolean; onClose: () => void } = $props();

  const SHORTCUTS: [string, string][] = [
    ['Space', 'Play / Pause'],
    ['Ctrl+Z / Ctrl+Shift+Z', 'Undo / Redo'],
    ['Delete / Backspace', 'Delete selected keyframe(s) or layer'],
    ['Ctrl+C / Ctrl+V', 'Copy / Paste keyframes at playhead'],
    ['Ctrl+Shift+V', 'Paste keyframes at original times'],
    ['Ctrl+D', 'Duplicate selected layer'],
    ['Escape', 'Clear selection'],
    ['← / →', 'Nudge playhead (or selected keyframes) by 1 frame'],
    ['Shift+← / Shift+→', 'Nudge by 1 second'],
    ['Ctrl+S', 'Save project'],
    ['Double-click a lane', 'Add a keyframe at that time'],
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
      <table>
        <tbody>
          {#each SHORTCUTS as [key, desc] (key)}
            <tr>
              <td class="key">{key}</td>
              <td>{desc}</td>
            </tr>
          {/each}
        </tbody>
      </table>
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
    padding: 16px 18px;
    width: 380px;
    max-width: calc(100vw - 32px);
    box-shadow: 0 12px 40px rgba(0, 0, 0, 0.5);
  }
  .modal h3 {
    margin: 0 0 10px;
    font-size: 14px;
    color: var(--text);
  }
  table {
    width: 100%;
    border-collapse: collapse;
    margin-bottom: 14px;
  }
  td {
    padding: 3px 0;
    font: 11px var(--mono);
    color: var(--dim);
  }
  td.key {
    color: var(--accent2);
    white-space: nowrap;
    padding-right: 12px;
  }
  .primary {
    width: 100%;
    background: var(--accent);
    color: #0a1420;
  }
</style>
