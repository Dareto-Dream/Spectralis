// Minimal hand-rolled reader/writer for the RIFF/WAV-style chunked binary
// container used by .spectralis (capsule) and .spectral (world) project
// files — see the "File format" section of the project plan for the full
// spec this implements. No dependency: it's just DataView/Uint8Array
// bookkeeping, small enough to hand-roll and keep in sync with the spec
// directly, matching this app's existing zero-runtime-dep philosophy.

export interface RawChunk {
  id: string; // always exactly 4 ASCII chars (space-padded)
  data: Uint8Array;
}

const textEncoder = new TextEncoder();
const textDecoder = new TextDecoder('utf-8');

export function padId(id: string): string {
  if (id.length > 4) throw new Error(`chunk id "${id}" is longer than 4 characters`);
  return id.padEnd(4, ' ');
}

function idBytes(id: string, out: Uint8Array, offset: number) {
  const padded = padId(id);
  for (let i = 0; i < 4; i++) out[offset + i] = padded.charCodeAt(i);
}

export function writeChunk(id: string, data: Uint8Array): Uint8Array {
  const needsPad = data.length % 2 === 1;
  const out = new Uint8Array(8 + data.length + (needsPad ? 1 : 0));
  idBytes(id, out, 0);
  new DataView(out.buffer).setUint32(4, data.length, true);
  out.set(data, 8);
  // trailing pad byte (if any) is left as 0, per spec §4
  return out;
}

// The only structural mechanism for hierarchy (§4.2) — a `LIST` chunk whose
// data is a 4-byte subtype tag followed by more chunks.
export function writeList(subtype: string, chunks: Uint8Array[]): Uint8Array {
  const bodyLen = chunks.reduce((n, c) => n + c.length, 0);
  const body = new Uint8Array(4 + bodyLen);
  idBytes(subtype, body, 0);
  let offset = 4;
  for (const c of chunks) {
    body.set(c, offset);
    offset += c.length;
  }
  return writeChunk('LIST', body);
}

export function concatBytes(parts: Uint8Array[]): Uint8Array {
  const total = parts.reduce((n, p) => n + p.length, 0);
  const out = new Uint8Array(total);
  let offset = 0;
  for (const p of parts) {
    out.set(p, offset);
    offset += p.length;
  }
  return out;
}

export interface ReadChunk {
  id: string;
  data: Uint8Array;
}

// Reads every chunk in [start, end) — callers decide what to keep vs. pass
// through untouched. This is the format's forward-compat mechanism (§4.1):
// an unrecognized chunk ID is never fatal, it's just data the reader hands
// back unexamined. A truncated trailing chunk stops the scan rather than
// throwing — callers get everything that parsed cleanly.
export function readChunks(bytes: Uint8Array, start: number, end: number): ReadChunk[] {
  const view = new DataView(bytes.buffer, bytes.byteOffset, bytes.byteLength);
  const out: ReadChunk[] = [];
  let offset = start;
  while (offset + 8 <= end) {
    const id = textDecoder.decode(bytes.subarray(offset, offset + 4));
    const size = view.getUint32(offset + 4, true);
    const dataStart = offset + 8;
    const dataEnd = dataStart + size;
    if (dataEnd > end) break;
    out.push({ id, data: bytes.slice(dataStart, dataEnd) });
    const pad = size % 2 === 1 ? 1 : 0;
    offset = dataEnd + pad;
  }
  return out;
}

export function readList(chunk: ReadChunk): { subtype: string; children: ReadChunk[] } {
  if (chunk.id !== 'LIST') throw new Error(`readList called on a non-LIST chunk ("${chunk.id}")`);
  const subtype = textDecoder.decode(chunk.data.subarray(0, 4));
  const children = readChunks(chunk.data, 4, chunk.data.length);
  return { subtype, children };
}

// --- length-prefixed UTF-8 strings (§5.2) ---

export function writeString(s: string): Uint8Array {
  const bytes = textEncoder.encode(s);
  if (bytes.length > 0xffff) throw new Error('string too long for a u16 length prefix');
  const out = new Uint8Array(2 + bytes.length);
  new DataView(out.buffer).setUint16(0, bytes.length, true);
  out.set(bytes, 2);
  return out;
}

export function readString(bytes: Uint8Array, offset: number): { value: string; next: number } {
  const length = new DataView(bytes.buffer, bytes.byteOffset, bytes.byteLength).getUint16(offset, true);
  const start = offset + 2;
  return { value: textDecoder.decode(bytes.subarray(start, start + length)), next: start + length };
}

// --- fixed-width scalar helpers, used by META/PATH/etc. field layouts ---

export function writeU16(n: number): Uint8Array {
  const b = new Uint8Array(2);
  new DataView(b.buffer).setUint16(0, n, true);
  return b;
}
export function readU16(bytes: Uint8Array, offset: number): number {
  return new DataView(bytes.buffer, bytes.byteOffset, bytes.byteLength).getUint16(offset, true);
}
export function writeU32(n: number): Uint8Array {
  const b = new Uint8Array(4);
  new DataView(b.buffer).setUint32(0, n, true);
  return b;
}
export function readU32(bytes: Uint8Array, offset: number): number {
  return new DataView(bytes.buffer, bytes.byteOffset, bytes.byteLength).getUint32(offset, true);
}
export function writeF32(n: number): Uint8Array {
  const b = new Uint8Array(4);
  new DataView(b.buffer).setFloat32(0, n, true);
  return b;
}
export function readF32(bytes: Uint8Array, offset: number): number {
  return new DataView(bytes.buffer, bytes.byteOffset, bytes.byteLength).getFloat32(offset, true);
}
// Timestamps (ms epoch) and other 64-bit integer fields.
export function writeI64(n: number): Uint8Array {
  const b = new Uint8Array(8);
  new DataView(b.buffer).setBigInt64(0, BigInt(Math.round(n)), true);
  return b;
}
export function readI64(bytes: Uint8Array, offset: number): number {
  return Number(new DataView(bytes.buffer, bytes.byteOffset, bytes.byteLength).getBigInt64(offset, true));
}

// --- CRC32 (standard IEEE 802.3 polynomial) — backs the optional trailing
// `CRC ` integrity chunk (§4.3). Nothing else in the format depends on it.
let crcTable: Uint32Array | null = null;
function getCrcTable(): Uint32Array {
  if (crcTable) return crcTable;
  const table = new Uint32Array(256);
  for (let n = 0; n < 256; n++) {
    let c = n;
    for (let k = 0; k < 8; k++) c = c & 1 ? 0xedb88320 ^ (c >>> 1) : c >>> 1;
    table[n] = c >>> 0;
  }
  crcTable = table;
  return table;
}
export function crc32(bytes: Uint8Array): number {
  const table = getCrcTable();
  let crc = 0xffffffff;
  for (let i = 0; i < bytes.length; i++) crc = table[(crc ^ bytes[i]) & 0xff] ^ (crc >>> 8);
  return (crc ^ 0xffffffff) >>> 0;
}

// --- file header (§3) ---

export interface FileHeaderInfo {
  magic: string;
  signature: string;
  bodyStart: number;
  bodyEnd: number;
}

// The header pair is closed and exhaustive (§3.1) — any other combination,
// including a magic paired with the OTHER format's signature, is corruption,
// never a format variant.
const VALID_PAIRS: Record<string, string> = { SPEX: 'pikl', SPWX: 'door' };

export function readFileHeader(bytes: Uint8Array): FileHeaderInfo {
  if (bytes.length < 12) throw new Error('not a Spectralis file: too short for a header');
  const magic = textDecoder.decode(bytes.subarray(0, 4));
  const fileSize = readU32(bytes, 4);
  const signature = textDecoder.decode(bytes.subarray(8, 12));
  const expected = VALID_PAIRS[magic];
  if (!expected) throw new Error(`not a Spectralis file: unrecognized magic "${magic}"`);
  if (expected !== signature) {
    throw new Error(`corrupt Spectralis file: magic "${magic}" doesn't match signature "${signature}" (expected "${expected}")`);
  }
  // fileSize = total length − 8 (§3, "total bytes following [the size] field") →
  // total length = fileSize + 8. Clamped to what we actually have in case of a
  // truncated file — readChunks then just stops early rather than reading OOB.
  return { magic, signature, bodyStart: 12, bodyEnd: Math.min(bytes.length, fileSize + 8) };
}

export function writeFileHeader(magic: string, signature: string, bodyLength: number): Uint8Array {
  const out = new Uint8Array(12);
  idBytes(magic, out, 0);
  new DataView(out.buffer).setUint32(4, 4 + bodyLength, true); // signature(4) + chunks
  idBytes(signature, out, 8);
  return out;
}
