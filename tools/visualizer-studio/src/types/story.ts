export interface StoryPage {
  id: string;
  text: string;
  speaker: string;
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
