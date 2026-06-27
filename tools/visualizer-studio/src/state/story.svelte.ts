// Story editor state — same lift-to-module-singleton rationale as
// state/world.svelte.ts (closable dockview panel + script console reach-in).
import { downloadText } from '../lib/downloadText';
import { toast } from './toast.svelte';
import { buildStoryHtml } from '../story-tab/buildStoryHtml';
import { buildStoryManifestFragment } from '../story-tab/buildStoryManifestFragment';
import type { StoryMeta, StoryPage } from '../types/story';

export class StoryStore {
  meta: StoryMeta = $state({ name: 'Narrator', portraitKey: 'portrait', charMs: 22, hue: 225 });
  pages: StoryPage[] = $state([
    { id: crypto.randomUUID(), speaker: '', text: 'Something happened before this song started...' },
  ]);

  outHtml = $state('');
  outManifest = $state('');
  previewSrcdoc = $state('');

  addPage(partial?: Partial<StoryPage>): StoryPage {
    this.pages.push({ id: partial?.id ?? crypto.randomUUID(), speaker: partial?.speaker ?? '', text: partial?.text ?? '' });
    return this.pages[this.pages.length - 1];
  }

  removePage(i: number) {
    this.pages.splice(i, 1);
  }

  reorderPage(from: number, to: number) {
    if (from === to || from < 0 || to < 0 || from >= this.pages.length || to >= this.pages.length) return;
    const [moved] = this.pages.splice(from, 1);
    this.pages.splice(to, 0, moved);
  }

  updateMeta(patch: Partial<StoryMeta>) {
    Object.assign(this.meta, patch);
  }

  generate() {
    this.outHtml = buildStoryHtml(this.meta, this.pages);
    this.outManifest = buildStoryManifestFragment(this.meta, this.pages);
    this.previewSrcdoc = this.outHtml.replace('<' + '/script>', ';window.spectral=window.spectral||{resume:function(){}};<' + '/script>');
    toast.push('success', 'Story files generated');
  }

  download() {
    downloadText('story_index.html', this.outHtml);
    downloadText('story_manifest_fragment.json', this.outManifest);
  }
}

export const storyStore = new StoryStore();
