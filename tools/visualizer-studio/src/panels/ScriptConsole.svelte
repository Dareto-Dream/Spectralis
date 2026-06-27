<script lang="ts">
  import type { ProjectStore } from '../state/project.svelte';
  import type { AudioState } from '../state/audio.svelte';
  import { worldStore } from '../state/world.svelte';
  import { storyStore } from '../state/story.svelte';
  import { assetLibrary } from '../state/assetLibrary.svelte';
  import { runScript } from '../lib/scriptRun';
  import { toast } from '../state/toast.svelte';
  import Play from '@lucide/svelte/icons/play';

  // audio is accepted for API symmetry with other dockview-mounted panels but
  // not currently exposed to scripts — nothing here needs it yet.
  let { store }: { store: ProjectStore; audio?: AudioState } = $props();

  type Target = 'studio' | 'world' | 'story';
  const TARGETS: { id: Target; label: string }[] = [
    { id: 'studio', label: 'Studio' },
    { id: 'world', label: 'World' },
    { id: 'story', label: 'Story' },
  ];

  const EXAMPLES: Record<Target, string> = {
    studio: `// ctx.studio: addLayer(type), setMeta(patch), addSection(), project (read-only snapshot)
for (let i = 0; i < 3; i++) {
  const layer = ctx.studio.addLayer('orb');
  console.log('added', layer.name);
}
console.log(ctx.studio.project.layers.length, 'layers total');`,
    world: `// ctx.world: addTrack(partial), setMeta(patch), tracks / meta (read-only), generate()
for (let i = 2; i <= 5; i++) {
  const n = i < 10 ? '0' + i : '' + i;
  ctx.world.addTrack({ id: 'track-' + n, title: 'Track ' + i, audio: 'tracks/' + n + '/audio.mp3' });
}
ctx.world.generate();
console.log(ctx.world.tracks.length, 'tracks now');`,
    story: `// ctx.story: addPage(partial), setMeta(patch), pages / meta (read-only), generate()
['They said it would be quiet.', 'It was not.'].forEach((text) => ctx.story.addPage({ text }));
ctx.story.generate();`,
  };

  const STORAGE_PREFIX = 'visualizer-studio:script:';
  let target: Target = $state('studio');
  let code = $state(loadCode('studio'));
  let logs: string[] = $state([]);
  let errorMsg: string | null = $state(null);
  let running = $state(false);

  function loadCode(t: Target): string {
    try {
      return localStorage.getItem(STORAGE_PREFIX + t) ?? EXAMPLES[t];
    } catch {
      return EXAMPLES[t];
    }
  }

  function switchTarget(t: Target) {
    saveCode();
    target = t;
    code = loadCode(t);
  }

  function saveCode() {
    try {
      localStorage.setItem(STORAGE_PREFIX + target, code);
    } catch {
      // best-effort — script text persistence is a convenience, never fatal
    }
  }

  function buildCtx() {
    return {
      studio: {
        addLayer: (type: Parameters<ProjectStore['addLayer']>[0]) => store.addLayer(type),
        setMeta: (patch: Parameters<ProjectStore['updateMeta']>[0]) => store.updateMeta(patch),
        addSection: () => store.addSection(),
        get project() {
          return $state.snapshot(store.project);
        },
      },
      world: {
        addTrack: (partial?: Parameters<typeof worldStore.addTrack>[0]) => worldStore.addTrack(partial),
        setMeta: (patch: Parameters<typeof worldStore.updateMeta>[0]) => worldStore.updateMeta(patch),
        generate: () => worldStore.generate(),
        get tracks() {
          return $state.snapshot(worldStore.tracks);
        },
        get meta() {
          return $state.snapshot(worldStore.meta);
        },
      },
      story: {
        addPage: (partial?: Parameters<typeof storyStore.addPage>[0]) => storyStore.addPage(partial),
        setMeta: (patch: Parameters<typeof storyStore.updateMeta>[0]) => storyStore.updateMeta(patch),
        generate: () => storyStore.generate(),
        get pages() {
          return $state.snapshot(storyStore.pages);
        },
        get meta() {
          return $state.snapshot(storyStore.meta);
        },
      },
      assets: {
        list: () => $state.snapshot(assetLibrary.assets).map((a) => ({ id: a.id, name: a.name, kind: a.kind })),
        addSvg: (name: string, svg: string) => assetLibrary.addSvg(name, svg),
      },
    };
  }

  function run() {
    running = true;
    saveCode();
    const result = runScript(code, { ctx: buildCtx() });
    logs = result.logs;
    errorMsg = result.error ?? null;
    running = false;
    if (result.ok) toast.push('success', 'Script ran');
    else toast.push('error', 'Script threw — see console output below');
  }

  function insertExample() {
    code = EXAMPLES[target];
  }

  function clearLog() {
    logs = [];
    errorMsg = null;
  }
</script>

<div class="console">
  <div class="toolbar">
    <div class="targets">
      {#each TARGETS as t (t.id)}
        <button class="small ghost" class:active={target === t.id} onclick={() => switchTarget(t.id)}>{t.label}</button>
      {/each}
    </div>
    <span class="spacer"></span>
    <button class="small ghost" onclick={insertExample} title="Reset the editor to a starter example for this target">Example</button>
    <button class="small primary" onclick={run} disabled={running}><Play size={12} /> Run</button>
  </div>
  <p class="hint">
    Plain browser JavaScript — not Node (no filesystem/network access; this ships as a static webpage). Your code
    receives a <code>ctx</code> object; the current target's API is shown in the Example snippet.
  </p>
  <textarea class="editor" bind:value={code} spellcheck="false" onblur={saveCode}></textarea>
  <div class="outputHead">
    <span>Output</span>
    {#if logs.length || errorMsg}<button class="small ghost" onclick={clearLog}>Clear</button>{/if}
  </div>
  <div class="output">
    {#each logs as line, i (i)}
      <div class="line">{line}</div>
    {/each}
    {#if errorMsg}
      <div class="line error">{errorMsg}</div>
    {/if}
    {#if !logs.length && !errorMsg}
      <div class="line dim">console.log output and errors appear here after Run.</div>
    {/if}
  </div>
</div>

<style>
  .console {
    display: flex;
    flex-direction: column;
    height: 100%;
    padding: 8px;
    gap: 6px;
  }
  .toolbar {
    display: flex;
    align-items: center;
    gap: 4px;
  }
  .targets {
    display: flex;
    gap: 2px;
  }
  .targets button.active {
    background: var(--bg3);
    color: var(--accent);
    border-color: var(--line2);
  }
  .spacer {
    flex: 1;
  }
  .hint {
    margin: 0;
    font: 10px var(--mono);
    color: var(--dim2);
    line-height: 1.4;
  }
  .hint code {
    color: var(--dim);
  }
  .editor {
    flex: 1 1 55%;
    min-height: 90px;
    resize: none;
    font: 11px var(--mono);
    line-height: 1.5;
    white-space: pre;
    tab-size: 2;
  }
  .outputHead {
    display: flex;
    align-items: center;
    justify-content: space-between;
    font: 10px var(--mono);
    color: var(--dim2);
    text-transform: uppercase;
    letter-spacing: 0.05em;
  }
  .output {
    flex: 1 1 30%;
    min-height: 60px;
    overflow-y: auto;
    background: var(--bg2);
    border: 1px solid var(--line);
    border-radius: 3px;
    padding: 6px 8px;
    font: 11px var(--mono);
  }
  .line {
    white-space: pre-wrap;
    word-break: break-word;
    padding: 1px 0;
    color: var(--text);
  }
  .line.error {
    color: var(--danger);
  }
  .line.dim {
    color: var(--dim2);
  }
</style>
