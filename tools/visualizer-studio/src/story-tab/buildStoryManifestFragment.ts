import type { StoryMeta, StoryPage } from '../types/story';

// Gap fix: the old tool's manifest fragment had no `story.pages` array at all —
// `speaker` existed on the in-memory page objects but was never threaded anywhere.
// `story.pages[].speaker` is what the capsule spec's synthesized-pager fallback
// actually reads from (confirmed against a real manifest using this field).
export function buildStoryManifestFragment(meta: StoryMeta, pages: StoryPage[]): string {
  const fragment = {
    story: {
      summary: 'Auto-generated story stub — edit this summary.',
      entry: 'story/index.html',
      binaryAssets: { [meta.portraitKey || 'portrait']: `story/assets/${meta.portraitKey || 'portrait'}.png` },
      pages: pages.map((p) => ({ text: p.text, speaker: p.speaker || meta.name })),
    },
  };
  return JSON.stringify(fragment, null, 2);
}
