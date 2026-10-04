import assert from 'node:assert/strict';
import { test } from 'node:test';
import { GB, MAX_BUDGET_BYTES } from './budget.mjs';
import { pruneFeed, runSync } from './sync-core.mjs';

const MB = 1_000_000;

/** An in-memory bucket that records every write, in order. */
function fakeStorage(initial = [], { lieAboutSize = false, extraGrowthOnPut = 0 } = {}) {
  const objects = new Map(initial.map((o) => [o.key, o.size]));
  const calls = [];
  return {
    calls,
    objects,
    total: () => [...objects.values()].reduce((n, s) => n + s, 0),
    async list() { return [...objects].map(([key, size]) => ({ key, size })); },
    async abortStaleUploads() { calls.push(['abort']); return 0; },
    async remove(keys) { calls.push(['remove', ...keys]); for (const k of keys) objects.delete(k); },
    async put(key, _body, size) { calls.push(['put', key]); objects.set(key, size + extraGrowthOnPut); },
    async sizeOf(key) { return objects.has(key) ? (lieAboutSize ? 1 : objects.get(key)) : null; },
  };
}

const pkgFiles = (version, { win = 350 * MB, linux = 285 * MB } = {}) => [
  { key: `Spectralis-${version}-win-x64-full.nupkg`, size: win, path: `/rel/Spectralis-${version}-win-x64-full.nupkg` },
  { key: `Spectralis-${version}-win-x64-delta.nupkg`, size: 5 * MB, path: `/rel/Spectralis-${version}-win-x64-delta.nupkg` },
  { key: `Spectralis-${version}-linux-x64-full.nupkg`, size: linux, path: `/rel/Spectralis-${version}-linux-x64-full.nupkg` },
];
const FEED_KEY = 'releases.win-x64.json';
const feedFile = { key: FEED_KEY, size: 3000, path: '/rel/releases.win-x64.json' };
const feedText = (versions) => JSON.stringify({
  Assets: versions.flatMap((v) => [
    { PackageId: 'Spectralis', Version: v, Type: 'Full', FileName: `Spectralis-${v}-win-x64-full.nupkg`, Size: 1 },
    { PackageId: 'Spectralis', Version: v, Type: 'Delta', FileName: `Spectralis-${v}-win-x64-delta.nupkg`, Size: 1 },
  ]),
});

test('feeds are pruned to the versions that still exist', () => {
  const pruned = JSON.parse(pruneFeed(feedText(['6.1.0', '6.0.0', '5.0.0']), ['6.1.0', '6.0.0']));

  assert.deepEqual([...new Set(pruned.Assets.map((a) => a.Version))], ['6.1.0', '6.0.0']);
  assert.equal(pruned.Assets.length, 4);
});

test('a feed that is not in the Velopack shape is left alone', () => {
  assert.equal(pruneFeed('{"something":"else"}', ['1.0.0']), '{"something":"else"}');
});

test('a legacy RELEASES file is pruned line by line, BOM and all', () => {
  const text = '﻿AAAA Spectralis-5.0.0-win-x64-full.nupkg 10\r\nBBBB Spectralis-6.1.0-win-x64-full.nupkg 20\r\nCCCC Spectralis-6.1.0-win-x64-delta.nupkg 5\r\n';

  assert.equal(
    pruneFeed(text, ['6.1.0']),
    'BBBB Spectralis-6.1.0-win-x64-full.nupkg 20\nCCCC Spectralis-6.1.0-win-x64-delta.nupkg 5\n',
  );
});

test('build logs that name local paths are not release artifacts', async () => {
  const storage = fakeStorage();
  const files = [...pkgFiles('6.1.0'), { key: 'assets.win-x64.json', size: 400, path: '/rel/assets.win-x64.json' }];

  await runSync({ storage, files, readFeed: async () => '{}', budgetBytes: 5 * GB, keepVersions: 4, apply: true });

  assert.ok(!storage.objects.has('assets.win-x64.json'));
});

test('a dry run measures and plans but writes nothing', async () => {
  const storage = fakeStorage();

  const result = await runSync({
    storage, files: [...pkgFiles('6.1.0'), feedFile], readFeed: async () => feedText(['6.1.0']),
    budgetBytes: 5 * GB, keepVersions: 4, apply: false,
  });

  assert.equal(result.ok, true);
  assert.equal(result.wrote, false);
  assert.deepEqual(storage.calls, []);
});

test('apply uploads packages first and the feed last, pruned to what is kept', async () => {
  const storage = fakeStorage();
  let feedBody = null;
  const original = storage.put.bind(storage);
  storage.put = async (key, body, size) => { if (key === FEED_KEY) feedBody = body.bytes.toString(); return original(key, body, size); };
  const files = [feedFile, ...['5.0.0', '5.1.0', '5.2.0', '5.3.0', '5.4.0', '6.1.0'].flatMap((v) => pkgFiles(v))];

  const result = await runSync({
    storage, files, readFeed: async () => feedText(['5.0.0', '5.1.0', '5.2.0', '5.3.0', '5.4.0', '6.1.0']),
    budgetBytes: 5 * GB, keepVersions: 4, apply: true,
  });

  assert.equal(result.ok, true);
  const puts = storage.calls.filter((c) => c[0] === 'put').map((c) => c[1]);
  assert.equal(puts.at(-1), FEED_KEY, 'the feed must be uploaded last');
  assert.ok(puts.every((k) => !/5\.0\.0|5\.1\.0/.test(k)), 'versions that would be pruned are never even uploaded');
  assert.deepEqual([...new Set(JSON.parse(feedBody).Assets.map((a) => a.Version))].sort(), ['5.2.0', '5.3.0', '5.4.0', '6.1.0']);
  assert.ok(storage.total() <= 5 * GB);
});

test('an over-budget request is refused before anything is touched', async () => {
  const storage = fakeStorage();

  const result = await runSync({
    storage, files: pkgFiles('6.1.0'), readFeed: async () => '{}', budgetBytes: 100 * MB, keepVersions: 4, apply: true,
  });

  assert.equal(result.ok, false);
  assert.equal(result.wrote, false);
  assert.deepEqual(storage.calls.filter((c) => c[0] !== 'abort'), [], 'no uploads and no deletes');
});

test('a budget above the hard maximum is clamped even when the caller asks for the moon', async () => {
  const storage = fakeStorage();

  const result = await runSync({
    storage, files: pkgFiles('6.1.0'), readFeed: async () => '{}', budgetBytes: 100 * GB, keepVersions: 4, apply: false,
  });

  assert.equal(result.plan.budgetBytes, MAX_BUDGET_BYTES);
});

test('if the bucket ends up bigger than planned the run reports it as a failure', async () => {
  // A misbehaving store that grows objects as they're written: the post-run measurement must catch it.
  const storage = fakeStorage([], { extraGrowthOnPut: 2 * GB });

  const result = await runSync({
    storage, files: pkgFiles('6.1.0'), readFeed: async () => '{}', budgetBytes: 1.5 * GB, keepVersions: 4, apply: true,
  }).catch((e) => ({ threw: e }));

  // Either verification (size mismatch) or the final measurement stops it; a silent success is the bug.
  assert.ok(result.threw || result.ok === false, 'a bucket over budget must never be reported as success');
});

test('an upload that does not verify is an error, not a success', async () => {
  const storage = fakeStorage([], { lieAboutSize: true });

  await assert.rejects(
    runSync({ storage, files: pkgFiles('6.1.0'), readFeed: async () => '{}', budgetBytes: 5 * GB, keepVersions: 4, apply: true }),
    /did not verify/,
  );
});

test('stale unfinished uploads are aborted before measuring, only when applying', async () => {
  const dry = fakeStorage();
  await runSync({ storage: dry, files: pkgFiles('6.1.0'), readFeed: async () => '{}', budgetBytes: 5 * GB, keepVersions: 4, apply: false });
  assert.deepEqual(dry.calls, []);

  const live = fakeStorage();
  await runSync({ storage: live, files: pkgFiles('6.1.0'), readFeed: async () => '{}', budgetBytes: 5 * GB, keepVersions: 4, apply: true });
  assert.equal(live.calls[0][0], 'abort');
});

test('files that are not release artifacts are never uploaded', async () => {
  const storage = fakeStorage();
  const files = [...pkgFiles('6.1.0'), { key: 'notes.txt', size: 10, path: '/rel/notes.txt' }, { key: 'RELEASES-win-x64.bak', size: 10, path: '/rel/x' }];

  await runSync({ storage, files, readFeed: async () => '{}', budgetBytes: 5 * GB, keepVersions: 4, apply: true });

  assert.ok(!storage.objects.has('notes.txt'));
  assert.ok(!storage.objects.has('RELEASES-win-x64.bak'));
});

test('re-running the same release is safe and leaves the bucket the same size', async () => {
  const storage = fakeStorage();
  const run = () => runSync({
    storage, files: [...pkgFiles('6.1.0'), feedFile], readFeed: async () => feedText(['6.1.0']),
    budgetBytes: 5 * GB, keepVersions: 4, apply: true,
  });

  await run();
  const first = storage.total();
  const second = await run();

  assert.equal(second.ok, true);
  assert.equal(storage.total(), first, 're-running the same release must not grow the bucket');
});

test('packages already in the bucket at the same size are not sent again, and do not count toward the peak', async () => {
  const storage = fakeStorage(pkgFiles('6.1.0').map(({ key, size }) => ({ key, size })));
  const before = storage.total();

  const result = await runSync({
    storage, files: [...pkgFiles('6.1.0'), ...pkgFiles('7.0.0')], readFeed: async () => '{}',
    budgetBytes: 1.8 * GB, keepVersions: 4, apply: true,
  });

  assert.equal(result.ok, true, 'only the new release needs room, not a second copy of the old one');
  assert.deepEqual(storage.calls.filter((c) => c[0] === 'put').map((c) => c[1]).sort(), pkgFiles('7.0.0').map((f) => f.key).sort());
  assert.ok(storage.total() > before);
});

test('a package whose size differs from the stored one is replaced', async () => {
  const [full] = pkgFiles('6.1.0');
  const storage = fakeStorage([{ key: full.key, size: full.size - 1 }]);

  await runSync({ storage, files: [full], readFeed: async () => '{}', budgetBytes: 5 * GB, keepVersions: 4, apply: true });

  assert.equal(storage.objects.get(full.key), full.size);
});
