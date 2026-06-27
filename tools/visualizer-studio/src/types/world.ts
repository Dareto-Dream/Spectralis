export interface WorldTrack {
  id: string;
  title: string;
  audio: string;
  cover: string;
  lrc: string;
  // Set when `cover` was populated by dragging an asset in from the Assets
  // docker — lets the row show a live thumbnail instead of just a path
  // string. Not part of the manifest schema; buildAlbumManifest only reads
  // the named fields above, so this is safely ignored on export.
  coverAssetId?: string;
}

export type WorldTemplateKind = 'tracklist' | 'levelmap';

export interface AlbumMeta {
  albumId: string;
  title: string;
  artist: string;
  year: number;
  templateKind: WorldTemplateKind;
}
