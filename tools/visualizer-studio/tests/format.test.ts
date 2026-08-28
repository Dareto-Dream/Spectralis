import { describe, expect, it } from 'vitest';
import {
  concatBytes,
  crc32,
  readChunks,
  readFileHeader,
  readString,
  writeChunk,
  writeFileHeader,
  writeList,
  writeString,
  writeU32,
} from '../src/format/riff';
import { decodeCapsuleFile, encodeCapsuleFile, newCapsuleMeta } from '../src/format/capsuleFile';
import { decodeWorldFile, encodeWorldFile, newWorldMeta } from '../src/format/worldFile';
import { newProject } from '../src/state/factories';

describe('riff.ts', () => {
  it('writeChunk/readChunks round-trips a single chunk byte-for-byte', () => {
    const data = new TextEncoder().encode('hello');
    const chunk = writeChunk('TEST', data);
    const [read] = readChunks(chunk, 0, chunk.length);
    expect(read.id).toBe('TEST');
    expect(new TextDecoder().decode(read.data)).toBe('hello');
  });

  it('pads odd-length chunk data so the next chunk starts on an even offset', () => {
    const odd = new TextEncoder().encode('abc'); // 3 bytes — odd
    const chunk = writeChunk('ODDX', odd);
    expect(chunk.length).toBe(8 + 3 + 1); // header + data + 1 pad byte
    const second = writeChunk('NEXT', new Uint8Array([1, 2]));
    const combined = concatBytes([chunk, second]);
    const chunks = readChunks(combined, 0, combined.length);
    expect(chunks.map((c) => c.id)).toEqual(['ODDX', 'NEXT']);
  });

  it('an unrecognized chunk ID is still returned by readChunks, never fatal', () => {
    const combined = concatBytes([writeChunk('UNKN', new Uint8Array([9])), writeChunk('META', new Uint8Array([1]))]);
    const chunks = readChunks(combined, 0, combined.length);
    expect(chunks.map((c) => c.id)).toEqual(['UNKN', 'META']);
  });

  it('writeList/readChunks nests a LIST with a readable subtype + children', () => {
    const list = writeList('TRAK', [writeChunk('META', new Uint8Array([1, 2, 3]))]);
    const [chunk] = readChunks(list, 0, list.length);
    expect(chunk.id).toBe('LIST');
    const subtype = new TextDecoder().decode(chunk.data.subarray(0, 4));
    expect(subtype).toBe('TRAK');
  });

  it('length-prefixed strings round-trip UTF-8 correctly, including multi-byte chars', () => {
    const encoded = writeString('héllo 🎧');
    const { value } = readString(encoded, 0);
    expect(value).toBe('héllo 🎧');
  });

  it('crc32 is stable and sensitive to any byte change', () => {
    const a = new TextEncoder().encode('the quick brown fox');
    const b = new TextEncoder().encode('the quick brown foy');
    expect(crc32(a)).toBe(crc32(a));
    expect(crc32(a)).not.toBe(crc32(b));
  });

  it('readFileHeader rejects a magic/signature mismatch as corruption, not a silent coercion', () => {
    const bad = writeFileHeader('SPEX', 'door', 0); // wrong pairing per the spec's table
    expect(() => readFileHeader(bad)).toThrow(/corrupt/i);
  });

  it('readFileHeader rejects an unrecognized magic entirely', () => {
    const bad = concatBytes([new TextEncoder().encode('JUNK'), writeU32(0), new TextEncoder().encode('door')]);
    expect(() => readFileHeader(bad)).toThrow(/not a Spectralis file/i);
  });
});

describe('capsuleFile.ts', () => {
  it('encode → decode preserves the project payload exactly', () => {
    const project = newProject();
    project.meta.title = 'Round Trip Track';
    const bytes = encodeCapsuleFile({
      meta: newCapsuleMeta({ title: 'Round Trip Track', artist: 'Someone', duration: 123.45 }),
      paths: { audio: 'C:/audio.mp3', cover: 'C:/cover.png' },
      project,
    });
    const decoded = decodeCapsuleFile(bytes);
    expect(decoded.project).toEqual(project);
    expect(decoded.meta.title).toBe('Round Trip Track');
    expect(decoded.meta.artist).toBe('Someone');
    expect(decoded.meta.duration).toBeCloseTo(123.45, 3);
    expect(decoded.paths.audio).toBe('C:/audio.mp3');
    expect(decoded.paths.cover).toBe('C:/cover.png');
  });

  it('preserves an unrecognized chunk (e.g. a future DISC/SYNC/SRCE) byte-for-byte across a round trip', () => {
    const project = newProject();
    const base = encodeCapsuleFile({ meta: newCapsuleMeta(), paths: { audio: '', cover: '' }, project });
    // Build a file that already carries a foreign chunk (simulating one the
    // main player app wrote), then round-trip it through this codec.
    const decodedFirst = decodeCapsuleFile(base);
    const withForeignChunk = encodeCapsuleFile({
      ...decodedFirst,
      passthrough: [...decodedFirst.passthrough, { id: 'SYNC', data: new Uint8Array([1, 2, 3, 4]) }],
    });
    const decodedSecond = decodeCapsuleFile(withForeignChunk);
    expect(decodedSecond.passthrough.some((c) => c.id === 'SYNC' && c.data.length === 4 && c.data[0] === 1)).toBe(true);

    // Saving again (as the Studio itself would, e.g. after an edit) must
    // still carry that foreign chunk forward untouched.
    const resaved = encodeCapsuleFile(decodedSecond);
    const decodedThird = decodeCapsuleFile(resaved);
    expect(decodedThird.passthrough.some((c) => c.id === 'SYNC')).toBe(true);
  });

  it('rejects a .spectral (World) file with a clear error instead of a generic parse failure', () => {
    const worldBytes = encodeWorldFile({ meta: newWorldMeta(), tracks: [], layout: [] });
    expect(() => decodeCapsuleFile(worldBytes)).toThrow(/World/);
  });
});

describe('worldFile.ts', () => {
  it('encode → decode round-trips meta, tracks, and layout', () => {
    const wf = {
      meta: newWorldMeta({ name: 'My World', author: 'Me' }),
      tracks: [
        { id: 'track-01', title: 'One', artist: 'Artist', audioPath: 'a.mp3', coverPath: 'a.png', lrcPath: '', viz: { note: 'stub' } },
        { id: 'track-02', title: 'Two', artist: 'Artist', audioPath: 'b.mp3', coverPath: '', lrcPath: 'b.lrc', viz: {} },
      ],
      layout: [
        { trackId: 'track-01', x: 1.5, y: -2.5, z: 0 },
        { trackId: 'track-02', x: 10, y: 20, z: 0 },
      ],
    };
    const bytes = encodeWorldFile(wf);
    const decoded = decodeWorldFile(bytes);
    expect(decoded.meta.name).toBe('My World');
    expect(decoded.tracks.map((t) => t.id)).toEqual(['track-01', 'track-02']);
    expect(decoded.tracks[0].audioPath).toBe('a.mp3');
    expect(decoded.tracks[0].viz).toEqual({ note: 'stub' });
    expect(decoded.layout[0].x).toBeCloseTo(1.5, 4);
    expect(decoded.layout[1].y).toBeCloseTo(20, 4);
  });

  it('rejects a .spectralis (Capsule) file with a clear error', () => {
    const project = newProject();
    const capsuleBytes = encodeCapsuleFile({ meta: newCapsuleMeta(), paths: { audio: '', cover: '' }, project });
    expect(() => decodeWorldFile(capsuleBytes)).toThrow(/Capsule/);
  });

  it('round-trips the node graph through the NGPH extension chunk (beyond the base spec, per §8 extensibility)', () => {
    const nodeGraph = [
      { id: 'node_1', name: 'Root', x: 10, y: 20, rotation: 0, scale: 1, assetIds: ['a1'], scriptIds: ['s1'], spritesheet: null, children: [] },
    ];
    const bytes = encodeWorldFile({ meta: newWorldMeta(), tracks: [], layout: [], nodeGraph });
    const decoded = decodeWorldFile(bytes);
    expect(decoded.nodeGraph).toEqual(nodeGraph);
  });

  it('defaults nodeGraph to an empty array when the file predates the NGPH chunk', () => {
    const bytes = encodeWorldFile({ meta: newWorldMeta(), tracks: [], layout: [] });
    const decoded = decodeWorldFile(bytes);
    expect(decoded.nodeGraph).toEqual([]);
  });
});
