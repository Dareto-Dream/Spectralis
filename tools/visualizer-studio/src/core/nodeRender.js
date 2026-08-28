// World's node graph renderer + script runtime — plain ES module, same
// reuse trick as core/render.js: the live editor's NodeCanvas.svelte imports
// this normally, and world-tab/buildNodeGraphHtml.ts inlines its literal
// source (via `?raw`) into the exported HTML, so the exported capsule runs
// EXACTLY the same node/script semantics the editor previewed, never a
// hand-copied reimplementation.
//
// Nodes render as real DOM elements (absolutely positioned divs with CSS
// transforms), not a <canvas> — that gets hover/click for free from the
// browser instead of hand-rolled hit-testing, and the exact same markup
// works whether it's live inside the Svelte app or sitting in a static
// exported .html file with no framework at all.

// `new Function` gives isolated variable scope for the injected node/on API,
// not a security sandbox — same trust model as lib/scriptRun.ts (the only
// person who can attach a script to a node is the person running the Studio).
export function runNodeScript(code, node, on) {
  const fn = new Function('node', 'on', code);
  fn(node, on);
}

// A live, mutation-triggering view over a plain node data object — assigning
// to `.x`/`.y`/`.rotation`/`.scale` re-applies the CSS transform immediately,
// so a script feels like it's "just" moving the node.
export function makeNodeApi(node, el, onChange) {
  const api = {
    get x() {
      return node.x;
    },
    set x(v) {
      node.x = v;
      applyNodeTransform(el, node);
      onChange && onChange();
    },
    get y() {
      return node.y;
    },
    set y(v) {
      node.y = v;
      applyNodeTransform(el, node);
      onChange && onChange();
    },
    get rotation() {
      return node.rotation;
    },
    set rotation(v) {
      node.rotation = v;
      applyNodeTransform(el, node);
      onChange && onChange();
    },
    get scale() {
      return node.scale;
    },
    set scale(v) {
      node.scale = v;
      applyNodeTransform(el, node);
      onChange && onChange();
    },
    name: node.name,
  };
  return api;
}

export function applyNodeTransform(el, node) {
  el.style.transform = `translate(-50%, -50%) translate(${node.x}px, ${node.y}px) rotate(${node.rotation}deg) scale(${node.scale})`;
}

// Sprite-sheet playback via CSS steps() — advances background-position across
// a `cols`x`rows` grid at `fps`. Pure CSS/rAF, no canvas needed for this either.
export function applySpritesheet(el, spritesheet, assetUrl) {
  if (!spritesheet || !assetUrl) return;
  const { cols, rows, frameW, frameH } = spritesheet;
  el.style.width = frameW + 'px';
  el.style.height = frameH + 'px';
  el.style.backgroundImage = `url(${assetUrl})`;
  el.style.backgroundRepeat = 'no-repeat';
  el.style.backgroundSize = `${cols * frameW}px ${rows * frameH}px`;
  const total = Math.max(1, cols * rows);
  let frame = 0;
  let last = 0;
  const fps = spritesheet.fps > 0 ? spritesheet.fps : 8;
  function tick(now) {
    if (!last) last = now;
    if (now - last >= 1000 / fps) {
      last = now;
      frame = (frame + 1) % total;
      const col = frame % cols;
      const row = Math.floor(frame / cols);
      el.style.backgroundPosition = `-${col * frameW}px -${row * frameH}px`;
    }
    el._spriteRaf = requestAnimationFrame(tick);
  }
  el._spriteRaf = requestAnimationFrame(tick);
}

export function stopSpritesheet(el) {
  if (el._spriteRaf) cancelAnimationFrame(el._spriteRaf);
}

// Builds one node's DOM element (and recursively its children), attaches its
// image assets, applies its spritesheet if configured, and wires its scripts
// — a self-contained "build + wire" pass rather than two separate walks, so
// scripts always see a fully-formed element to operate on.
//
// `resolveAsset(id)` -> { dataUrl, kind } | null
// `resolveScript(id)` -> source string | null
export function buildNodeElement(node, resolveAsset, resolveScript, doc) {
  const el = doc.createElement('div');
  el.className = 'sp-node';
  el.dataset.nodeId = node.id;
  el.style.position = 'absolute';
  el.style.left = '0';
  el.style.top = '0';
  el.style.transformOrigin = 'center center';
  applyNodeTransform(el, node);

  const spriteAssetId = node.spritesheet && node.spritesheet.assetId;
  if (spriteAssetId) {
    const asset = resolveAsset(spriteAssetId);
    if (asset) applySpritesheet(el, node.spritesheet, asset.dataUrl);
  } else {
    for (const assetId of node.assetIds || []) {
      const asset = resolveAsset(assetId);
      if (!asset || asset.kind === 'audio' || asset.kind === 'script') continue;
      const img = doc.createElement('img');
      img.src = asset.dataUrl;
      img.draggable = false;
      img.style.display = 'block';
      img.style.maxWidth = '120px';
      img.style.maxHeight = '120px';
      img.style.pointerEvents = 'none';
      el.appendChild(img);
    }
  }

  const label = doc.createElement('div');
  label.className = 'sp-node-label';
  label.textContent = node.name;
  el.appendChild(label);

  for (const child of node.children || []) {
    el.appendChild(buildNodeElement(child, resolveAsset, resolveScript, doc));
  }

  const scriptIds = node.scriptIds || [];
  if (scriptIds.length) {
    el.style.cursor = 'pointer';
    const handlers = { hover: [], unhover: [], click: [] };
    const on = (event, handler) => {
      if (handlers[event]) handlers[event].push(handler);
    };
    const api = makeNodeApi(node, el);
    for (const scriptId of scriptIds) {
      const src = resolveScript(scriptId);
      if (!src) continue;
      try {
        runNodeScript(src, api, on);
      } catch (err) {
        // A broken script on one node shouldn't take down the rest of the
        // world — same "best effort, never fatal" convention as the rest of
        // this app's script surfaces.
        // eslint-disable-next-line no-console
        console.error('[node script] ' + node.name + ': ' + (err && err.message ? err.message : err));
      }
    }
    el.addEventListener('mouseenter', () => handlers.hover.forEach((h) => h(api)));
    el.addEventListener('mouseleave', () => handlers.unhover.forEach((h) => h(api)));
    el.addEventListener('click', () => handlers.click.forEach((h) => h(api)));
  }

  return el;
}

export function renderNodeTree(container, nodes, resolveAsset, resolveScript) {
  container.innerHTML = '';
  for (const node of nodes) {
    container.appendChild(buildNodeElement(node, resolveAsset, resolveScript, container.ownerDocument));
  }
}
