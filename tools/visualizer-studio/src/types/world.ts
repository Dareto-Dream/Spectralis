export interface WorldTrack {
  id: string;
  title: string;
  audio: string;
  cover: string;
  lrc: string;
}

export type WorldTemplateKind = 'tracklist' | 'levelmap';

export interface AlbumMeta {
  albumId: string;
  title: string;
  artist: string;
  year: number;
  templateKind: WorldTemplateKind;
}
