<script lang="ts">
  // World mode's primary workspace — a node-based scene graph rendered as
  // real DOM elements (see core/nodeRender.js for why: hover/click come free
  // from the browser, and the exact same builder function runs in the
  // exported HTML). This component owns structural rebuilds + editor-only
  // drag-to-move; live transform mutation (drag OR an attached script's
  // hover handler) writes straight into the reactive SceneNode objects via
  // core/nodeRender.js's applyNodeTransform, so the Inspector's fields and
  // the DOM stay in sync automatically without a manual RAF loop.
  import { nodeWorldStore } from '../state/nodeWorld.svelte';
  import { assetLibrary } from '../state/assetLibrary.svelte';
  import { buildNodeElement, applyNodeTransform } from '../core/nodeRender.js';
  import { openContextMenu, type ContextMenuItem } from '../timeline/contextMenu.svelte';
  import type { SceneNode } from '../types/node';
  import Plus from '@lucide/svelte/icons/plus';

  let container: HTMLDivElement | undefined = $state();

  function resolveAsset(id: string) {
    const a = assetLibrary.get(id);
    return a ? { dataUrl: a.dataUrl, kind: a.kind } : null;
  }
  function resolveScript(id: string) {
    return assetLibrary.getScriptSource(id);
  }

  // Rebuild the DOM only when shape/attachments/spritesheet config change —
  // NOT on every x/y/rotation/scale tick, which are applied live in place
  // instead (see attachDragHandlers below and makeNodeApi in nodeRender.js).
  // A full rebuild on every drag frame would also kill any in-flight
  // spritesheet rAF loop and any script-registered handlers for no reason.
  function signature(nodes: SceneNode[]): unknown {
    return nodes.map((n) => [n.id, n.name, n.assetIds, n.scriptIds, n.spritesheet, signature(n.children)]);
  }

  function attachDragHandlers(el: HTMLElement, node: SceneNode) {
    let dragging = false;
    let moved = false;
    let startX = 0;
    let startY = 0;
    let startNodeX = 0;
    let startNodeY = 0;

    el.addEventListener('pointerdown', (e: PointerEvent) => {
      if (e.button !== 0) return;
      dragging = true;
      moved = false;
      startX = e.clientX;
      startY = e.clientY;
      startNodeX = node.x;
      startNodeY = node.y;
      el.setPointerCapture(e.pointerId);
      nodeWorldStore.selectedId = node.id;
      e.stopPropagation(); // don't let an ancestor node's own drag handler also start
    });
    el.addEventListener('pointermove', (e: PointerEvent) => {
      if (!dragging) return;
      const dx = e.clientX - startX;
      const dy = e.clientY - startY;
      if (Math.abs(dx) > 3 || Math.abs(dy) > 3) moved = true;
      node.x = startNodeX + dx;
      node.y = startNodeY + dy;
      applyNodeTransform(el, node);
      e.stopPropagation();
    });
    el.addEventListener('pointerup', (e: PointerEvent) => {
      dragging = false;
      // A drag that actually moved shouldn't also fire the node's attached
      // click-script (built into nodeRender.js's element) — swallow exactly
      // the one click that a mouseup naturally generates after this.
      if (moved) {
        const suppressClick = (ce: MouseEvent) => {
          ce.stopImmediatePropagation();
          el.removeEventListener('click', suppressClick, true);
        };
        el.addEventListener('click', suppressClick, true);
      }
      e.stopPropagation();
    });
  }

  function attachDragRecursive(el: HTMLElement, node: SceneNode) {
    attachDragHandlers(el, node);
    const childEls = Array.from(el.children).filter((c) => (c as HTMLElement).classList?.contains('sp-node'));
    node.children.forEach((child, i) => {
      const childEl = childEls[i] as HTMLElement | undefined;
      if (childEl) attachDragRecursive(childEl, child);
    });
  }

  function rebuild() {
    if (!container) return;
    container.innerHTML = '';
    for (const node of nodeWorldStore.roots) {
      const el = buildNodeElement(node, resolveAsset, resolveScript, document);
      attachDragRecursive(el, node);
      container.appendChild(el);
    }
  }

  let lastSig = '';
  $effect(() => {
    const sig = JSON.stringify(signature(nodeWorldStore.roots));
    if (sig !== lastSig) {
      lastSig = sig;
      rebuild();
    }
  });

  // Only deselect on a click that lands on the empty stage itself — a click
  // that started on a node bubbles here too (unlike the drag handlers above,
  // which stopPropagation on pointer* but not on the resulting click), so
  // checking e.target rather than relying on propagation keeps this simple.
  function onBackgroundClick(e: MouseEvent) {
    if (e.target === container) nodeWorldStore.selectedId = null;
  }

  function onContextMenu(e: MouseEvent) {
    e.preventDefault();
    const items: ContextMenuItem[] = [{ label: 'Add Root Node', action: () => nodeWorldStore.addNode(null) }];
    const selected = nodeWorldStore.selectedId;
    if (selected) {
      items.push(
        { label: 'Add Child Node', action: () => nodeWorldStore.addNode(selected) },
        { label: 'Duplicate', action: () => nodeWorldStore.duplicateNode(selected) },
        { label: 'Delete', danger: true, action: () => nodeWorldStore.deleteNode(selected) }
      );
    }
    openContextMenu(e.clientX, e.clientY, items);
  }
</script>

<div class="nodeCanvasWrap">
  <div class="toolbar">
    <button class="small ghost" onclick={() => nodeWorldStore.addNode(null)}><Plus size={12} /> Node</button>
    <span class="hint">Right-click for more · drag a node to move it · click empty space to deselect</span>
  </div>
  <!-- svelte-ignore a11y_click_events_have_key_events -->
  <!-- svelte-ignore a11y_no_static_element_interactions -->
  <div class="stage" bind:this={container} onclick={onBackgroundClick} oncontextmenu={onContextMenu}></div>
</div>

<style>
  .nodeCanvasWrap {
    display: flex;
    flex-direction: column;
    height: 100%;
  }
  .toolbar {
    display: flex;
    align-items: center;
    gap: 8px;
    padding: 4px 8px;
    border-bottom: 1px solid var(--line);
    flex-shrink: 0;
  }
  .hint {
    font: 10px var(--mono);
    color: var(--dim2);
  }
  .stage {
    position: relative;
    isolation: isolate;
    flex: 1;
    overflow: auto;
    background: var(--bg0);
    background-image: radial-gradient(var(--line) 1px, transparent 1px);
    background-size: 24px 24px;
    background-position: center;
  }
  .stage :global(.sp-node) {
    display: flex;
    flex-direction: column;
    align-items: center;
    gap: 2px;
    padding: 6px;
  }
  .stage :global(.sp-node-label) {
    font: 10px var(--mono);
    color: var(--text);
    background: var(--bg2);
    border: 1px solid var(--line);
    border-radius: 3px;
    padding: 1px 5px;
    white-space: nowrap;
    pointer-events: none;
  }
  .stage :global(.sp-node:not(:has(img))) {
    width: 48px;
    height: 48px;
    justify-content: center;
    background: var(--bg2);
    border: 1px dashed var(--line2);
    border-radius: 4px;
  }
</style>
