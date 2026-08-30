// .spex (capsule) file codec — magic `SPEX`/signature `pikl`. This is
// Visualizer Studio's own working project file, saved/opened directly by
// File > Save/Load Project (src/lib/projectSave.ts) and by the autosave
// safety net. NOT the same thing as the `.spectralis` extension used
// elsewhere in this codebase (export/buildPackScript.ts) — that's the real
// signed SPCC-v3 capsule produced once you EXPORT and run the generated pack
// script; the two are unrelated container formats that happen to be
// documented in the same product-level spec (docs/formats/spectralis-
// capsule.md), not the same file. A capsule represents a single preserved
// track/session. Visualizer Studio's own project data (layers, sections,
// tracks/keyframes — everything in src/types/project.ts) travels as-is,
// unchanged, inside the opaque `VIZP` chunk; this module only owns the
// container around it.
//
// `DISC`/`SYNC`/`SRCE` (Discord Rich Presence, Shared Play session state,
// source provenance) belong to the main Spectralis player app, not the
// Studio — this codec never authors them, but preserves whatever it finds
// byte-for-byte across a load/save round trip via `passthrough`, per the
// format's forward-compat rule: a tool must never silently drop data it
// doesn't own.
import {
  concatBytes,
  crc32,
  readChunks,
  readFileHeader,
  readI64,
  readString,
  writeChunk,
  writeFileHeader,
  writeI64,
  writeString,
  writeU16,
  readU16,
  type RawChunk,
} from './riff';
import type { Project } from '../types/project';
import { migrateProject } from '../lib/migrate';

export interface CapsuleMeta {
  title: string;
  artist: string;
  duration: number; // seconds
  createdAt: number; // ms epoch
  modifiedAt: number; // ms epoch
  appVersion: string;
}

export interface CapsulePaths {
  audio: string; // may be '' if no audio is attached
  cover: string; // may be '' if no cover is attached
}

export interface CapsuleFile {
  meta: CapsuleMeta;
  paths: CapsulePaths;
  project: Project;
  passthrough: RawChunk[];
}

// Visualizer Studio's own container-format version — independent of
// `Project.formatVersion`, which versions the VIZP payload's own schema and
// already has its own migration path (migrateProject). A major bump here
// means an incompatible CHUNK layout, not a project-schema change.
const FORMAT_VERSION = { major: 1, minor: 0 };
const APP_VERSION = '0.1.0';

function encodeVersion(): Uint8Array {
  return concatBytes([writeU16(FORMAT_VERSION.major), writeU16(FORMAT_VERSION.minor)]);
}

// Field layout (this tool's own choice — the spec names the fields per chunk
// but not their exact binary layout): title, artist, duration(f64 seconds via
// two u32? — kept simple as ms i64 for integer precision), created/modified
// (ms i64 each), appVersion string. Order is fixed; a stricter reader can
// stop after the fields it knows and ignore trailing bytes.
function encodeMeta(meta: CapsuleMeta): Uint8Array {
  return concatBytes([
    writeString(meta.title),
    writeString(meta.artist),
    writeI64(Math.round(meta.duration * 1000)),
    writeI64(meta.createdAt),
    writeI64(meta.modifiedAt),
    writeString(meta.appVersion),
  ]);
}

function decodeMeta(data: Uint8Array): CapsuleMeta {
  let { value: title, next } = readString(data, 0);
  const artistRead = readString(data, next);
  const artist = artistRead.value;
  next = artistRead.next;
  const durationMs = readI64(data, next);
  next += 8;
  const createdAt = readI64(data, next);
  next += 8;
  const modifiedAt = readI64(data, next);
  next += 8;
  const appVersionRead = readString(data, next);
  return { title, artist, duration: durationMs / 1000, createdAt, modifiedAt, appVersion: appVersionRead.value };
}

function encodePaths(paths: CapsulePaths): Uint8Array {
  return concatBytes([writeString(paths.audio), writeString(paths.cover)]);
}

function decodePaths(data: Uint8Array): CapsulePaths {
  const audioRead = readString(data, 0);
  const coverRead = readString(data, audioRead.next);
  return { audio: audioRead.value, cover: coverRead.value };
}

export function encodeCapsuleFile(cf: Omit<CapsuleFile, 'passthrough'> & { passthrough?: RawChunk[] }): Uint8Array {
  const chunks: Uint8Array[] = [
    writeChunk('SPvN', encodeVersion()),
    writeChunk('META', encodeMeta(cf.meta)),
    writeChunk('PATH', encodePaths(cf.paths)),
    writeChunk('VIZP', new TextEncoder().encode(JSON.stringify(cf.project))),
    ...(cf.passthrough ?? []).map((c) => writeChunk(c.id, c.data)),
  ];
  const payload = concatBytes(chunks);
  const withCrc = concatBytes([payload, writeChunk('CRC ', crc32Bytes(payload))]);
  return concatBytes([writeFileHeader('SPEX', 'pikl', withCrc.length), withCrc]);
}

function crc32Bytes(payload: Uint8Array): Uint8Array {
  const b = new Uint8Array(4);
  new DataView(b.buffer).setUint32(0, crc32(payload), true);
  return b;
}

export function newCapsuleMeta(partial: Partial<CapsuleMeta> = {}): CapsuleMeta {
  const now = Date.now();
  return {
    title: '',
    artist: '',
    duration: 0,
    createdAt: now,
    modifiedAt: now,
    appVersion: APP_VERSION,
    ...partial,
  };
}

export function decodeCapsuleFile(bytes: Uint8Array): CapsuleFile {
  const header = readFileHeader(bytes);
  if (header.magic !== 'SPEX') {
    throw new Error(`this is a ${header.magic === 'SPWX' ? 'World' : 'non-Spectralis'} file, not a Capsule (.spex) file`);
  }
  const chunks = readChunks(bytes, header.bodyStart, header.bodyEnd);

  const versionChunk = chunks.find((c) => c.id === 'SPvN');
  if (versionChunk) {
    const major = readU16(versionChunk.data, 0);
    if (major > FORMAT_VERSION.major) {
      throw new Error(`this capsule file was saved by a newer version of Visualizer Studio (format v${major}.x) — please update`);
    }
  }

  const metaChunk = chunks.find((c) => c.id === 'META');
  const pathChunk = chunks.find((c) => c.id === 'PATH');
  const vizpChunk = chunks.find((c) => c.id === 'VIZP');
  if (!metaChunk || !vizpChunk) throw new Error('this capsule file is missing required META/VIZP chunks');

  const meta = decodeMeta(metaChunk.data);
  const paths = pathChunk ? decodePaths(pathChunk.data) : { audio: '', cover: '' };
  const project = migrateProject(JSON.parse(new TextDecoder().decode(vizpChunk.data)));

  // Everything else — DISC/SYNC/SRCE, `CRC `, unknown IDs — is preserved for
  // the next encode. `JUNK` is explicitly deleted padding (§4.3) and is
  // always dropped, never round-tripped.
  const OWNED = new Set(['SPvN', 'META', 'PATH', 'VIZP', 'JUNK', 'CRC ']);
  const passthrough: RawChunk[] = chunks.filter((c) => !OWNED.has(c.id)).map((c) => ({ id: c.id, data: c.data }));

  return { meta, paths, project, passthrough };
}
