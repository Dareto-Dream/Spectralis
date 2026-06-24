// Shared by the top bar's "Import LRC…" file input AND the lyrics inspector
// panel's drop zone (plan QoL §C/§D) — one implementation of the parse +
// skipped-line counting + toast feedback, not two copies that can drift.
import type { ProjectStore } from '../state/project.svelte';
import { toast } from '../state/toast.svelte';
import { parseLRC, flattenWords } from '../core/lrc.js';

export async function importLrcFile(store: ProjectStore, file: File): Promise<void> {
  const raw = await file.text();
  const nonBlankLines = raw.trim().split('\n').filter((l) => l.trim()).length;

  let lyricsLayer = store.project.layers.find((l) => l.type === 'lyrics');
  const keySet: Record<string, boolean> = {};
  if (lyricsLayer?.type === 'lyrics' && lyricsLayer.params.keyWords) {
    for (const w of lyricsLayer.params.keyWords.split(/[\s,]+/)) {
      if (w) keySet[w.toLowerCase()] = true;
    }
  }
  const lines = parseLRC(raw);
  const skipped = Math.max(0, nonBlankLines - lines.length);
  const words = flattenWords(lines, keySet);
  if (!words.length) {
    toast.push('error', 'No lyric lines recognized in that file');
    return;
  }
  if (!lyricsLayer) lyricsLayer = store.addLayer('lyrics');
  if (lyricsLayer.type === 'lyrics') lyricsLayer.params.words = words;
  store.project.importedLrcRaw = raw;
  store.commit();
  toast.push(
    'success',
    skipped > 0 ? `Imported ${words.length} words, skipped ${skipped} unrecognized lines` : `Imported ${words.length} words from ${file.name}`
  );
}
