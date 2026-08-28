<script lang="ts">
  // Editor for a script ASSET's content (a node-attached .spc script) — a
  // different surface from ScriptConsole.svelte, which runs one-off macros
  // against the studio/world/story stores. This is authoring persistent
  // content that lives in the Assets docker and gets attached to nodes.
  import { focusTrap } from '../lib/focusTrap';
  import { assetLibrary } from '../state/assetLibrary.svelte';
  import { uiState } from '../state/uiState.svelte';
  import { toast } from '../state/toast.svelte';
  import CodeEditor from './CodeEditor.svelte';

  const asset = $derived(uiState.editingScriptAssetId ? assetLibrary.get(uiState.editingScriptAssetId) : undefined);
  let code = $state('');
  let loadedForId: string | null = null;

  $effect(() => {
    if (asset && asset.id !== loadedForId) {
      loadedForId = asset.id;
      code = assetLibrary.getScriptSource(asset.id) ?? '';
    }
  });

  function save() {
    if (!asset) return;
    assetLibrary.updateScriptContent(asset.id, code);
    toast.push('success', `Saved ${asset.name}`);
  }

  function close() {
    if (asset) save();
    uiState.editingScriptAssetId = null;
    loadedForId = null;
  }

  function onKeydown(e: KeyboardEvent) {
    if (e.key === 'Escape') close();
  }
</script>

{#if asset}
  <!-- svelte-ignore a11y_click_events_have_key_events -->
  <!-- svelte-ignore a11y_no_static_element_interactions -->
  <div class="scrim" onclick={close}>
    <div class="modal" role="dialog" aria-modal="true" tabindex="-1" onclick={(e) => e.stopPropagation()} onkeydown={onKeydown} use:focusTrap>
      <div class="head">
        <h3>{asset.name}</h3>
        <span class="hint">Ctrl+Enter has no effect here (this is a saved script, not a console) — just Save when you're done.</span>
      </div>
      <div class="editorWrap">
        <CodeEditor bind:value={code} />
      </div>
      <div class="actions">
        <button class="small ghost" onclick={close}>Close</button>
        <button class="small primary" onclick={save}>Save</button>
      </div>
    </div>
  </div>
{/if}

<style>
  .scrim {
    position: fixed;
    inset: 0;
    background: rgba(0, 0, 0, 0.55);
    display: flex;
    align-items: center;
    justify-content: center;
    z-index: 3000;
  }
  .modal {
    width: min(640px, 92vw);
    height: min(520px, 82vh);
    display: flex;
    flex-direction: column;
    gap: 8px;
    background: var(--bg1);
    border: 1px solid var(--line2);
    border-radius: 6px;
    padding: 14px;
    box-shadow: 0 20px 60px rgba(0, 0, 0, 0.6);
  }
  .head {
    display: flex;
    flex-direction: column;
    gap: 2px;
  }
  h3 {
    margin: 0;
    font: 13px var(--mono);
    color: var(--text);
  }
  .hint {
    font: 10px var(--mono);
    color: var(--dim2);
  }
  .editorWrap {
    flex: 1;
    min-height: 0;
  }
  .actions {
    display: flex;
    justify-content: flex-end;
    gap: 6px;
  }
</style>
