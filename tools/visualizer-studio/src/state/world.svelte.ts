// World editor state, lifted out of WorldPanel.svelte into a module singleton
// (same pattern as toast/confirmModal) for two reasons: the panel itself is
// now a closable/reopenable dockview panel, and a component-local `$state`
// would reset every time it closes and reopens; and the script console (Tools
// > Script Console) needs to reach this data from OUTSIDE the component tree.
import { downloadText } from '../lib/downloadText';
import { toast } from './toast.svelte';
import { buildWorldHtml, buildAlbumManifest, worldPreviewSrcdoc } from '../world-tab/buildWorldHtml';
import type { AlbumMeta, WorldTrack } from '../types/world';

export class WorldStore {
  meta: AlbumMeta = $state({ albumId: 'my-album-2026', title: 'Album Title', artist: 'Artist Name', year: 2026, templateKind: 'tracklist' });
  tracks: WorldTrack[] = $state([
    { id: 'track-01', title: 'Track One', audio: 'tracks/01/audio.mp3', cover: 'tracks/01/cover.png', lrc: 'tracks/01/lyrics.lrc' },
  ]);

  outHtml = $state('');
  outManifest = $state('');
  previewSrcdoc = $state('');

  addTrack(partial?: Partial<WorldTrack>): WorldTrack {
    const n = this.tracks.length + 1;
    const padded = n < 10 ? `0${n}` : String(n);
    const track: WorldTrack = {
      id: partial?.id ?? `track-${padded}`,
      title: partial?.title ?? `Track ${n}`,
      audio: partial?.audio ?? `tracks/${padded}/audio.mp3`,
      cover: partial?.cover ?? `tracks/${padded}/cover.png`,
      lrc: partial?.lrc ?? '',
    };
    this.tracks.push(track);
    return this.tracks[this.tracks.length - 1];
  }

  removeTrack(i: number) {
    this.tracks.splice(i, 1);
  }

  // Drag-to-reorder counterpart, mirrors ProjectStore.reorderLayer's pattern.
  reorderTrack(from: number, to: number) {
    if (from === to || from < 0 || to < 0 || from >= this.tracks.length || to >= this.tracks.length) return;
    const [moved] = this.tracks.splice(from, 1);
    this.tracks.splice(to, 0, moved);
  }

  updateMeta(patch: Partial<AlbumMeta>) {
    Object.assign(this.meta, patch);
  }

  generate() {
    this.outHtml = buildWorldHtml(this.meta.templateKind, this.tracks);
    this.outManifest = buildAlbumManifest(this.meta, this.tracks);
    this.previewSrcdoc = worldPreviewSrcdoc(this.outHtml, this.meta.title, this.meta.artist);
    toast.push('success', 'World files generated — copy from the panels below');
  }

  download() {
    downloadText('world_index.html', this.outHtml);
    downloadText(`${this.meta.albumId || 'album'}_manifest.json`, this.outManifest);
  }
}

export const worldStore = new WorldStore();
