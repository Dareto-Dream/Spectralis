// Exports the World node graph to a single self-contained HTML file — same
// "inline the literal core/*.js source, no duplicate hand-copy" trick
// src/export/buildVisualizerHtml.ts already uses for Capsule, applied to
// core/nodeRender.js instead: the exported file runs the EXACT SAME
// buildNodeElement/script-wiring code the editor's NodeCanvas previewed.
import nodeRenderSrc from '../core/nodeRender.js?raw';
import { assetLibrary } from '../state/assetLibrary.svelte';
import type { SceneNode } from '../types/node';
import type { NodeWorldMeta } from '../state/nodeWorld.svelte';
import { esc } from '../lib/esc';

function stripModuleSyntax(src: string): string {
  return src
    .replace(/^import\s[\s\S]*?;\s*$/gm, '')
    .replace(/^export\s+/gm, '')
    .trim();
}

function collectIds(nodes: SceneNode[], assetIds: Set<string>, scriptIds: Set<string>) {
  for (const n of nodes) {
    for (const id of n.assetIds) assetIds.add(id);
    for (const id of n.scriptIds) scriptIds.add(id);
    if (n.spritesheet) assetIds.add(n.spritesheet.assetId);
    collectIds(n.children, assetIds, scriptIds);
  }
}

export function buildNodeGraphHtml(meta: NodeWorldMeta, roots: SceneNode[]): string {
  const assetIds = new Set<string>();
  const scriptIds = new Set<string>();
  collectIds(roots, assetIds, scriptIds);

  // Only the assets/scripts actually referenced by this graph ship — not the
  // whole library, which can hold plenty of unrelated in-progress work.
  const assetsById: Record<string, { dataUrl: string; kind: string }> = {};
  for (const id of assetIds) {
    const a = assetLibrary.get(id);
    if (a) assetsById[id] = { dataUrl: a.dataUrl, kind: a.kind };
  }
  const scriptsById: Record<string, string> = {};
  for (const id of scriptIds) {
    const src = assetLibrary.getScriptSource(id);
    if (src) scriptsById[id] = src;
  }

  const driver = [
    '(function(){',
    `var NODES = ${JSON.stringify(roots)};`,
    `var ASSETS = ${JSON.stringify(assetsById)};`,
    `var SCRIPTS = ${JSON.stringify(scriptsById)};`,
    stripModuleSyntax(nodeRenderSrc),
    'function resolveAsset(id) { return ASSETS[id] || null; }',
    'function resolveScript(id) { return SCRIPTS[id] || null; }',
    'document.addEventListener("DOMContentLoaded", function () {',
    '  renderNodeTree(document.getElementById("sp-world-stage"), NODES, resolveAsset, resolveScript);',
    '});',
    '})();',
  ].join('\n');

  return [
    '<!doctype html>',
    '<html>',
    '<head>',
    '<meta charset="utf-8" />',
    `<title>${esc(meta.name || 'Spectralis World')}</title>`,
    '<style>',
    'html,body{margin:0;height:100%;background:#05050a;overflow:hidden;}',
    '#sp-world-stage{position:relative;width:100%;height:100%;}',
    '.sp-node{display:flex;flex-direction:column;align-items:center;gap:2px;}',
    '.sp-node-label{font:10px monospace;color:#e7e7ee;background:#1e1e27;border:1px solid #3a3a47;border-radius:3px;padding:1px 5px;white-space:nowrap;pointer-events:none;}',
    '</style>',
    '</head>',
    '<body>',
    '<div id="sp-world-stage"></div>',
    '<script>' + driver + '<' + '/script>',
    '</body>',
    '</html>',
  ].join('\n');
}
