<script lang="ts">
  // Shared CodeMirror 6 host — the "basic IDE features" (line numbers, syntax
  // highlighting, bracket matching, undo history, search) for both the
  // Script Console (studio/world/story macros) and the Script asset editor
  // (node-attached .spc scripts). One component so both stay visually and
  // behaviorally consistent instead of duplicating CodeMirror setup twice.
  import { onMount, onDestroy } from 'svelte';
  import { EditorView, basicSetup } from 'codemirror';
  import { javascript } from '@codemirror/lang-javascript';
  import { EditorState, Compartment } from '@codemirror/state';
  import { keymap } from '@codemirror/view';

  let {
    value = $bindable(''),
    onRun,
  }: {
    value: string;
    // Ctrl/Cmd+Enter is the universal "run this" gesture in every script
    // surface this backs — wiring it here means neither call site needs its
    // own keydown listener just for that one shortcut.
    onRun?: () => void;
  } = $props();

  let container: HTMLDivElement | undefined = $state();
  let view: EditorView | undefined;
  const editable = new Compartment();
  let applyingExternalValue = false;

  const theme = EditorView.theme(
    {
      '&': { height: '100%', fontSize: '11px', backgroundColor: 'var(--bg2)', color: 'var(--text)' },
      '.cm-content': { fontFamily: 'var(--mono)', caretColor: 'var(--accent)' },
      '.cm-gutters': { backgroundColor: 'var(--bg2)', color: 'var(--dim2)', border: 'none' },
      '.cm-activeLine': { backgroundColor: 'var(--bg3)' },
      '.cm-activeLineGutter': { backgroundColor: 'var(--bg3)' },
      '&.cm-focused': { outline: 'none' },
      '.cm-selectionBackground, ::selection': { backgroundColor: 'rgba(127,183,255,0.25) !important' },
      '.cm-scroller': { overflow: 'auto' },
    },
    { dark: true }
  );

  onMount(() => {
    if (!container) return;
    view = new EditorView({
      parent: container,
      state: EditorState.create({
        doc: value,
        extensions: [
          basicSetup,
          javascript(),
          theme,
          editable.of(EditorView.editable.of(true)),
          keymap.of([
            {
              key: 'Mod-Enter',
              run: () => {
                onRun?.();
                return true;
              },
            },
          ]),
          EditorView.updateListener.of((update) => {
            if (update.docChanged && !applyingExternalValue) value = update.state.doc.toString();
          }),
        ],
      }),
    });
  });

  // External updates to `value` (e.g. switching which script the console is
  // editing) — only push into CodeMirror when it's not just an echo of the
  // editor's own last keystroke, or every keystroke would round-trip through
  // a full doc replace and fight the cursor position.
  $effect(() => {
    const next = value;
    if (!view) return;
    if (next === view.state.doc.toString()) return;
    applyingExternalValue = true;
    view.dispatch({ changes: { from: 0, to: view.state.doc.length, insert: next } });
    applyingExternalValue = false;
  });

  onDestroy(() => view?.destroy());
</script>

<div class="codeEditorHost" bind:this={container}></div>

<style>
  .codeEditorHost {
    height: 100%;
    overflow: hidden;
    border: 1px solid var(--line);
    border-radius: 3px;
  }
  .codeEditorHost :global(.cm-editor) {
    height: 100%;
  }
</style>
