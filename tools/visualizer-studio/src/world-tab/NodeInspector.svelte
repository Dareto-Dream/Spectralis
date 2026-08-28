<script lang="ts">
  // Property panel for the node selected in NodeCanvas — transform fields
  // (scrubbable, same convention as Capsule's PropRow), attached
  // assets/scripts (toggled from the full asset library), and the entry
  // point into the Sprite Editor. Nodes aren't keyframed ("scripts for
  // worlds, keyframes for capsules"), so there's no timeline/track UI here —
  // just the current live values a script or a drag can move.
  import { nodeWorldStore } from '../state/nodeWorld.svelte';
  import { assetLibrary } from '../state/assetLibrary.svelte';
  import { dockManager } from '../panels/dockManager.svelte';
  import { scrubbable } from '../lib/scrubbableNumber';
  import Image from '@lucide/svelte/icons/image';
  import FileCode from '@lucide/svelte/icons/file-code';
  import Film from '@lucide/svelte/icons/film';
  import X from '@lucide/svelte/icons/x';

  const node = $derived(nodeWorldStore.selectedNode);
  const attachableAssets = $derived(assetLibrary.assets.filter((a) => a.kind === 'image' || a.kind === 'svg'));
  const attachableScripts = $derived(assetLibrary.assets.filter((a) => a.kind === 'script'));

  function field(key: 'x' | 'y' | 'rotation' | 'scale') {
    return {
      value: node ? node[key] : 0,
      step: key === 'scale' ? 0.05 : key === 'rotation' ? 1 : 1,
      onChange: (v: number) => node && nodeWorldStore.updateTransform(node.id, { [key]: v }),
      onCommit: () => {},
    };
  }
</script>

<div class="nodeInspector">
  {#if !node}
    <p class="hint">Select a node in the workspace, or right-click it to add one.</p>
  {:else}
    <div class="head">
      <input class="name" value={node.name} onchange={(e) => nodeWorldStore.rename(node.id, (e.target as HTMLInputElement).value)} />
      <button class="icon ghost danger" title="Delete node" aria-label="Delete node" onclick={() => nodeWorldStore.deleteNode(node.id)}>
        <X size={13} />
      </button>
    </div>

    <div class="transformGrid">
      {#each [['x', 'X'], ['y', 'Y'], ['rotation', 'Rot'], ['scale', 'Scale']] as [key, label] (key)}
        <label class="transformField">
          <span class="scrubLabel" use:scrubbable={field(key as 'x' | 'y' | 'rotation' | 'scale')}>{label}</span>
          <input
            type="number"
            step={key === 'scale' ? 0.05 : 1}
            value={node[key as 'x' | 'y' | 'rotation' | 'scale']}
            onchange={(e) => nodeWorldStore.updateTransform(node.id, { [key]: parseFloat((e.target as HTMLInputElement).value) || 0 })}
          />
        </label>
      {/each}
    </div>

    <div class="section">
      <p class="sectionLabel"><Image size={11} /> Attached assets</p>
      {#if !attachableAssets.length}
        <p class="hint small">No image/SVG assets yet — add one in the Assets docker.</p>
      {:else}
        <div class="chipList">
          {#each attachableAssets as a (a.id)}
            <button class="chip" class:on={node.assetIds.includes(a.id)} onclick={() => nodeWorldStore.toggleAsset(node.id, a.id)}>
              {a.name}
            </button>
          {/each}
        </div>
      {/if}
    </div>

    <div class="section">
      <p class="sectionLabel"><FileCode size={11} /> Attached scripts</p>
      {#if !attachableScripts.length}
        <p class="hint small">
          No scripts yet — add one from Assets > Templates > New Script, or the Assets toolbar.
        </p>
      {:else}
        <div class="chipList">
          {#each attachableScripts as s (s.id)}
            <button class="chip" class:on={node.scriptIds.includes(s.id)} onclick={() => nodeWorldStore.toggleScript(node.id, s.id)}>
              {s.name}
            </button>
          {/each}
        </div>
      {/if}
    </div>

    <div class="section">
      <button class="small ghost" onclick={() => dockManager.focusOrOpen('spriteEditor')}>
        <Film size={12} /> {node.spritesheet ? 'Edit Spritesheet' : 'Configure Spritesheet'}
      </button>
      {#if node.spritesheet}
        <button class="small ghost danger" onclick={() => nodeWorldStore.setSpritesheet(node.id, null)}>Remove</button>
      {/if}
    </div>
  {/if}
</div>

<style>
  .nodeInspector {
    padding: 8px;
    display: flex;
    flex-direction: column;
    gap: 10px;
  }
  .hint {
    font: 11px var(--mono);
    color: var(--dim);
    line-height: 1.5;
  }
  .hint.small {
    font-size: 10px;
    color: var(--dim2);
  }
  .head {
    display: flex;
    align-items: center;
    gap: 4px;
  }
  .name {
    flex: 1;
    font: 12px var(--mono);
  }
  .transformGrid {
    display: grid;
    grid-template-columns: 1fr 1fr;
    gap: 6px;
    border-top: 1px solid var(--line);
    border-bottom: 1px solid var(--line);
    padding: 6px 0;
  }
  .transformField {
    display: flex;
    align-items: center;
    gap: 4px;
  }
  .scrubLabel {
    width: 32px;
    font: 10px var(--mono);
    color: var(--dim);
    cursor: ew-resize;
  }
  .scrubLabel:hover,
  .scrubLabel.scrubbing {
    color: var(--accent);
  }
  .transformField input {
    flex: 1;
    width: 0;
  }
  .section {
    display: flex;
    flex-direction: column;
    gap: 4px;
  }
  .sectionLabel {
    margin: 0;
    display: flex;
    align-items: center;
    gap: 4px;
    font: 10px var(--mono);
    color: var(--dim2);
    text-transform: uppercase;
    letter-spacing: 0.05em;
  }
  .chipList {
    display: flex;
    flex-wrap: wrap;
    gap: 4px;
  }
  .chip {
    font: 10px var(--mono);
    padding: 2px 6px;
    border-radius: 10px;
    border: 1px solid var(--line);
    background: var(--bg2);
    color: var(--dim);
  }
  .chip.on {
    background: var(--accent2);
    color: #1a1400;
    border-color: var(--accent2);
  }
</style>
