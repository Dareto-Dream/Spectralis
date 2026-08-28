<script lang="ts">
  // Dedicated spritesheet configuration viewport (plan: "nodes can have
  // attached animations, spritesheet configured in a dedicated
  // editor/viewport"). Slices an image asset into a cols×rows grid and
  // attaches the resulting config to the currently-selected node —
  // core/nodeRender.js's applySpritesheet() plays it back via CSS
  // background-position stepping, identically in the editor and export.
  import { nodeWorldStore } from '../state/nodeWorld.svelte';
  import { assetLibrary } from '../state/assetLibrary.svelte';
  import { toast } from '../state/toast.svelte';
  import type { SpritesheetConfig } from '../types/node';

  const node = $derived(nodeWorldStore.selectedNode);
  const imageAssets = $derived(assetLibrary.assets.filter((a) => a.kind === 'image'));

  let assetId = $state('');
  let cols = $state(4);
  let rows = $state(1);
  let frameW = $state(64);
  let frameH = $state(64);
  let fps = $state(8);
  let loadedForNodeId: string | null = $state(null);

  // Load the node's existing config (if any) exactly once when selection
  // changes — after that, edits here are a scratch draft until Apply, so
  // switching fields doesn't fight a re-derivation from the node every time.
  $effect(() => {
    if (node && node.id !== loadedForNodeId) {
      loadedForNodeId = node.id;
      if (node.spritesheet) {
        assetId = node.spritesheet.assetId;
        cols = node.spritesheet.cols;
        rows = node.spritesheet.rows;
        frameW = node.spritesheet.frameW;
        frameH = node.spritesheet.frameH;
        fps = node.spritesheet.fps;
      } else {
        assetId = '';
        cols = 4;
        rows = 1;
        frameW = 64;
        frameH = 64;
        fps = 8;
      }
    }
  });

  const previewAsset = $derived(assetId ? assetLibrary.get(assetId) : undefined);

  function apply() {
    if (!node || !assetId) return;
    const config: SpritesheetConfig = { assetId, cols, rows, frameW, frameH, fps };
    nodeWorldStore.setSpritesheet(node.id, config);
    toast.push('success', `Spritesheet applied to "${node.name}"`);
  }
</script>

<div class="spriteEditor">
  {#if !node}
    <p class="hint">Select a node in the World workspace first — spritesheets are configured per node.</p>
  {:else}
    <label class="field">
      <span>Source image</span>
      <select bind:value={assetId}>
        <option value="">— choose an image asset —</option>
        {#each imageAssets as a (a.id)}
          <option value={a.id}>{a.name}</option>
        {/each}
      </select>
    </label>

    {#if previewAsset}
      <div class="previewWrap">
        <div
          class="previewImg"
          style="width:{cols * frameW}px; height:{rows * frameH}px; background-image:url({previewAsset.dataUrl}); background-size:{cols * frameW}px {rows * frameH}px;"
        >
          <div class="grid" style="grid-template-columns: repeat({cols}, {frameW}px); grid-template-rows: repeat({rows}, {frameH}px);">
            {#each { length: cols * rows } as _, i (i)}
              <div class="cell"></div>
            {/each}
          </div>
        </div>
      </div>
    {/if}

    <div class="fields">
      <label class="num">Cols <input type="number" min="1" bind:value={cols} /></label>
      <label class="num">Rows <input type="number" min="1" bind:value={rows} /></label>
      <label class="num">Frame W <input type="number" min="1" bind:value={frameW} /></label>
      <label class="num">Frame H <input type="number" min="1" bind:value={frameH} /></label>
      <label class="num">FPS <input type="number" min="1" bind:value={fps} /></label>
    </div>

    <button class="small primary" onclick={apply} disabled={!assetId}>Apply to "{node.name}"</button>
  {/if}
</div>

<style>
  .spriteEditor {
    padding: 8px;
    display: flex;
    flex-direction: column;
    gap: 10px;
    overflow: auto;
    height: 100%;
  }
  .hint {
    font: 11px var(--mono);
    color: var(--dim);
    line-height: 1.5;
  }
  .field {
    display: flex;
    flex-direction: column;
    gap: 3px;
    font: 10px var(--mono);
    color: var(--dim2);
  }
  .previewWrap {
    overflow: auto;
    border: 1px solid var(--line);
    border-radius: 4px;
    background: var(--bg0);
    padding: 8px;
  }
  .previewImg {
    position: relative;
    image-rendering: pixelated;
  }
  .grid {
    display: grid;
    width: 100%;
    height: 100%;
  }
  .cell {
    border: 1px dashed rgba(127, 183, 255, 0.5);
  }
  .fields {
    display: grid;
    grid-template-columns: 1fr 1fr;
    gap: 6px;
  }
  .num {
    display: flex;
    align-items: center;
    justify-content: space-between;
    gap: 4px;
    font: 10px var(--mono);
    color: var(--dim);
  }
  .num input {
    width: 60px;
  }
</style>
