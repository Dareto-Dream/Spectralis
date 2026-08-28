// .spectral (world) file codec — magic `SPWX`/signature `door`. A world is a
// set of per-track mini-capsules (`LIST`/`TRAK` groups, each with its own
// META/PATH/VIZP) plus a spatial layout. This is intentionally a thin,
// easy-to-extend stub: World mode's real per-track scene content (the node
// graph — see the plan's "Future phases") doesn't exist yet, so each track's
// VIZP is just a small placeholder object for now. The container shape is
// still worth getting right early since it's what the node graph phase will
// slot its real payload into without another format change.
//
// `OBSV`/`THME` belong to the main Spectralis player app; like capsuleFile's
// DISC/SYNC/SRCE, they're preserved byte-for-byte via `passthrough`, never
// authored here.
import {
  concatBytes,
  crc32,
  readChunks,
  readF32,
  readFileHeader,
  readI64,
  readList,
  readString,
  readU16,
  writeChunk,
  writeF32,
  writeFileHeader,
  writeI64,
  writeList,
  writeString,
  writeU16,
  type RawChunk,
  type ReadChunk,
} from './riff';

export interface WorldMeta {
  name: string;
  author: string;
  createdAt: number;
  modifiedAt: number;
}

export interface WorldTrackFile {
  id: string; // stable identifier (§7.2) — layout records address tracks by this, not position
  title: string;
  artist: string;
  audioPath: string;
  coverPath: string;
  // Extension beyond the base spec's PATH (audio+cover only) — a lyrics file
  // path is exactly the same kind of thing, and unrecognized trailing fields
  // are safe for any reader that stops after the fields it knows.
  lrcPath: string;
  // Placeholder for the future per-track node-graph payload. Kept as an
  // opaque JSON blob (like capsule's VIZP) so its shape can grow without
  // another container change.
  viz: unknown;
}

export interface WorldLayoutEntry {
  trackId: string;
  x: number;
  y: number;
  z: number;
}

export interface WorldFile {
  meta: WorldMeta;
  tracks: WorldTrackFile[];
  layout: WorldLayoutEntry[];
  // The node graph (state/nodeWorld.svelte.ts) — an addition beyond the base
  // spec's per-track-album model, stored as its own top-level chunk (`NGPH`).
  // The format is explicitly extensible this way (§8: new chunk types may be
  // added at any time; unrecognized ones are just skipped), so this doesn't
  // touch the spec's own chunk set at all. `unknown` here, not `SceneNode[]`,
  // to avoid a state/ -> format/ import for a type-only reference — callers
  // narrow it themselves (see nodeWorld.svelte.ts's save/load wiring).
  nodeGraph: unknown;
  passthrough: RawChunk[];
}

const FORMAT_VERSION = { major: 1, minor: 0 };

function encodeVersion(): Uint8Array {
  return concatBytes([writeU16(FORMAT_VERSION.major), writeU16(FORMAT_VERSION.minor)]);
}

function encodeMeta(meta: WorldMeta): Uint8Array {
  return concatBytes([writeString(meta.name), writeString(meta.author), writeI64(meta.createdAt), writeI64(meta.modifiedAt)]);
}
function decodeMeta(data: Uint8Array): WorldMeta {
  const nameRead = readString(data, 0);
  const authorRead = readString(data, nameRead.next);
  const createdAt = readI64(data, authorRead.next);
  const modifiedAt = readI64(data, authorRead.next + 8);
  return { name: nameRead.value, author: authorRead.value, createdAt, modifiedAt };
}

function encodeTrackMeta(t: WorldTrackFile): Uint8Array {
  return concatBytes([writeString(t.id), writeString(t.title), writeString(t.artist)]);
}
function decodeTrackMeta(data: Uint8Array): Pick<WorldTrackFile, 'id' | 'title' | 'artist'> {
  const idRead = readString(data, 0);
  const titleRead = readString(data, idRead.next);
  const artistRead = readString(data, titleRead.next);
  return { id: idRead.value, title: titleRead.value, artist: artistRead.value };
}

function encodeTrackPath(t: WorldTrackFile): Uint8Array {
  return concatBytes([writeString(t.audioPath), writeString(t.coverPath), writeString(t.lrcPath)]);
}
function decodeTrackPath(data: Uint8Array): Pick<WorldTrackFile, 'audioPath' | 'coverPath' | 'lrcPath'> {
  const audioRead = readString(data, 0);
  const coverRead = readString(data, audioRead.next);
  const lrcRead = readString(data, coverRead.next);
  return { audioPath: audioRead.value, coverPath: coverRead.value, lrcPath: lrcRead.value };
}

function encodeTrack(t: WorldTrackFile): Uint8Array {
  return writeList('TRAK', [
    writeChunk('META', encodeTrackMeta(t)),
    writeChunk('PATH', encodeTrackPath(t)),
    writeChunk('VIZP', new TextEncoder().encode(JSON.stringify(t.viz ?? {}))),
  ]);
}

function decodeTrack(list: { subtype: string; children: ReadChunk[] }): WorldTrackFile {
  const metaChunk = list.children.find((c) => c.id === 'META');
  const pathChunk = list.children.find((c) => c.id === 'PATH');
  const vizpChunk = list.children.find((c) => c.id === 'VIZP');
  const metaPart = metaChunk ? decodeTrackMeta(metaChunk.data) : { id: crypto.randomUUID(), title: '', artist: '' };
  const pathPart = pathChunk ? decodeTrackPath(pathChunk.data) : { audioPath: '', coverPath: '', lrcPath: '' };
  const viz = vizpChunk ? JSON.parse(new TextDecoder().decode(vizpChunk.data)) : {};
  return { ...metaPart, ...pathPart, viz };
}

function encodeLayout(layout: WorldLayoutEntry[]): Uint8Array {
  return concatBytes([
    writeU16(layout.length),
    ...layout.flatMap((e) => [writeString(e.trackId), writeF32(e.x), writeF32(e.y), writeF32(e.z)]),
  ]);
}
function decodeLayout(data: Uint8Array): WorldLayoutEntry[] {
  const count = readU16(data, 0);
  const out: WorldLayoutEntry[] = [];
  let offset = 2;
  for (let i = 0; i < count; i++) {
    const idRead = readString(data, offset);
    const x = readF32(data, idRead.next);
    const y = readF32(data, idRead.next + 4);
    const z = readF32(data, idRead.next + 8);
    out.push({ trackId: idRead.value, x, y, z });
    offset = idRead.next + 12;
  }
  return out;
}

export function encodeWorldFile(
  wf: Omit<WorldFile, 'passthrough' | 'nodeGraph'> & { passthrough?: RawChunk[]; nodeGraph?: unknown }
): Uint8Array {
  const chunks: Uint8Array[] = [
    writeChunk('SPvN', encodeVersion()),
    writeChunk('META', encodeMeta(wf.meta)),
    ...wf.tracks.map(encodeTrack),
    writeChunk('LYT ', encodeLayout(wf.layout)),
    ...(wf.nodeGraph !== undefined ? [writeChunk('NGPH', new TextEncoder().encode(JSON.stringify(wf.nodeGraph)))] : []),
    ...(wf.passthrough ?? []).map((c) => writeChunk(c.id, c.data)),
  ];
  const payload = concatBytes(chunks);
  const crc = new Uint8Array(4);
  new DataView(crc.buffer).setUint32(0, crc32(payload), true);
  const withCrc = concatBytes([payload, writeChunk('CRC ', crc)]);
  return concatBytes([writeFileHeader('SPWX', 'door', withCrc.length), withCrc]);
}

export function decodeWorldFile(bytes: Uint8Array): WorldFile {
  const header = readFileHeader(bytes);
  if (header.magic !== 'SPWX') {
    throw new Error(`this is a ${header.magic === 'SPEX' ? 'Capsule' : 'non-Spectralis'} file, not a World (.spectral) file`);
  }
  const chunks = readChunks(bytes, header.bodyStart, header.bodyEnd);

  const versionChunk = chunks.find((c) => c.id === 'SPvN');
  if (versionChunk) {
    const major = readU16(versionChunk.data, 0);
    if (major > FORMAT_VERSION.major) {
      throw new Error(`this world file was saved by a newer version of Visualizer Studio (format v${major}.x) — please update`);
    }
  }

  const metaChunk = chunks.find((c) => c.id === 'META');
  const layoutChunk = chunks.find((c) => c.id === 'LYT ');
  const nodeGraphChunk = chunks.find((c) => c.id === 'NGPH');
  const trackLists = chunks.filter((c) => c.id === 'LIST').map(readList).filter((l) => l.subtype === 'TRAK');

  const meta = metaChunk ? decodeMeta(metaChunk.data) : { name: '', author: '', createdAt: Date.now(), modifiedAt: Date.now() };
  const tracks = trackLists.map(decodeTrack);
  const layout = layoutChunk ? decodeLayout(layoutChunk.data) : [];
  const nodeGraph = nodeGraphChunk ? JSON.parse(new TextDecoder().decode(nodeGraphChunk.data)) : [];

  const OWNED = new Set(['SPvN', 'META', 'LIST', 'LYT ', 'NGPH', 'JUNK', 'CRC ']);
  const passthrough: RawChunk[] = chunks.filter((c) => !OWNED.has(c.id)).map((c) => ({ id: c.id, data: c.data }));

  return { meta, tracks, layout, nodeGraph, passthrough };
}

export function newWorldMeta(partial: Partial<WorldMeta> = {}): WorldMeta {
  const now = Date.now();
  return { name: 'Untitled World', author: '', createdAt: now, modifiedAt: now, ...partial };
}
