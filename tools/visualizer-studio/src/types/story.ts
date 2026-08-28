export interface StoryPage {
  id: string;
  text: string;
  speaker: string;
  // Advanced-mode-only fields (StoryPanel.svelte) — a per-page accent hue,
  // separate from the story-wide `StoryMeta.hue`. Not read by
  // buildStoryHtml/buildStoryManifestFragment today (the shipped page
  // contract is single-hue), so this is authoring-only until a later pass
  // teaches the driver to use it — same "extra field, safely ignored"
  // convention as world.ts's `coverAssetId`.
  hue?: number;
}

export interface StoryMeta {
  name: string;
  portraitKey: string;
  charMs: number;
  hue: number;
  // Set when portraitKey was populated by dragging an asset in from the
  // Assets docker — lets the UI show a live thumbnail. Not read by
  // buildStoryHtml/buildStoryManifestFragment, so it's ignored on export.
  portraitAssetId?: string;
}
