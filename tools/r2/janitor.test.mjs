import assert from 'node:assert/strict';
import { test } from 'node:test';
import { GB, MAX_BUDGET_BYTES } from './budget.mjs';
import worker, { listAll, runJanitor } from './janitor.mjs';

const MB = 1_000_000;

/** A fake R2 bucket binding: paginated list(), batch delete(). */
function fakeBucket(objects, pageSize = 1000) {
  const store = new Map(objects.map((o, i) => [o.key, { key: o.key, size: o.size, uploaded: new Date(Date.parse('2026-01-01') + i * 1000) }]));
  const deleted = [];
  return {
    deleted,
    store,
    async list({ cursor, limit = 1000 } = {}) {
      const all = [...store.values()];
      const start = cursor ? Number(cursor) : 0;
      const slice = all.slice(start, start + Math.min(limit, pageSize));
      const next = start + slice.length;
      return { objects: slice, truncated: next < all.length, cursor: String(next) };
    },
    async delete(keys) {
      for (const k of Array.isArray(keys) ? keys : [keys]) { store.delete(k); deleted.push(k); }
    },
  };
}

const pkg = (v, rid, type, size) => ({ key: `Spectralis-${v}-${rid}-${type}.nupkg`, size });
const release = (v, size = 300 * MB) => [pkg(v, 'win-x64', 'full', size), pkg(v, 'win-x64', 'delta', 5 * MB), pkg(v, 'linux-x64', 'full', size)];
const feeds = () => [{ key: 'releases.win-x64.json', size: 9000 }, { key: 'RELEASES', size: 7000 }];
const quiet = () => {};

test('listing follows the cursor across pages', async () => {
  const bucket = fakeBucket(Array.from({ length: 25 }, (_, i) => ({ key: `junk-${i}`, size: 1 })), 10);

  const all = await listAll(bucket);

  assert.equal(all.length, 25);
});

test('a healthy bucket is left completely alone', async () => {
  const bucket = fakeBucket([...release('6.1.0'), ...release('6.0.0'), ...feeds()]);

  const plan = await runJanitor(bucket, {}, quiet);

  assert.equal(plan.action, 'none');
  assert.deepEqual(bucket.deleted, []);
});

test('over the ceiling it deletes junk, then old releases, and keeps feeds and the newest versions', async () => {
  const bucket = fakeBucket([
    ...['5.0.0', '5.1.0', '5.2.0', '6.0.0'].flatMap((v) => release(v, 900 * MB)),
    ...feeds(),
    { key: 'Spectralis-win-x64-Setup.exe', size: 300 * MB },
    { key: 'stray-upload.zip', size: 1 * GB },
  ]);

  const plan = await runJanitor(bucket, { CEILING_BYTES: 4 * GB, TARGET_BYTES: 3 * GB }, quiet);

  assert.equal(plan.action, 'prune');
  assert.equal(bucket.deleted[0], 'stray-upload.zip', 'unknown junk goes first');
  for (const key of ['releases.win-x64.json', 'RELEASES', 'Spectralis-win-x64-Setup.exe', 'Spectralis-6.0.0-win-x64-full.nupkg', 'Spectralis-5.2.0-win-x64-full.nupkg']) {
    assert.ok(bucket.store.has(key), `${key} must survive`);
  }
  const remaining = [...bucket.store.values()].reduce((n, o) => n + o.size, 0);
  assert.ok(remaining <= 4 * GB, `${remaining} should be under the ceiling`);
});

test('dry run logs what it would do and deletes nothing', async () => {
  const bucket = fakeBucket([{ key: 'junk.bin', size: 9 * GB }, ...feeds()]);
  const lines = [];

  const plan = await runJanitor(bucket, { DRY_RUN: 'true' }, (l) => lines.push(l));

  assert.equal(plan.dryRun, true);
  assert.deepEqual(bucket.deleted, []);
  assert.ok(lines.some((l) => l.includes('DRY_RUN')));
});

test('a ceiling configured above the hard maximum does not weaken protection', async () => {
  const bucket = fakeBucket([{ key: 'junk.bin', size: 9 * GB }]);

  const plan = await runJanitor(bucket, { CEILING_BYTES: 50 * GB }, quiet);

  assert.equal(plan.action, 'prune');
  assert.ok(MAX_BUDGET_BYTES < 9 * GB);
  assert.deepEqual(bucket.deleted, ['junk.bin']);
});

test('it says so loudly when nothing safe is left to remove', async () => {
  const bucket = fakeBucket([...release('6.0.0', 5 * GB), ...feeds()]);
  const lines = [];

  const plan = await runJanitor(bucket, { CEILING_BYTES: 6 * GB }, (l) => lines.push(l));

  assert.deepEqual(bucket.deleted, []);
  assert.ok(plan.stillOverBy > 0);
  assert.ok(lines.some((l) => l.includes('ERROR')));
});

test('the scheduled handler runs the janitor against the BUCKET binding', async () => {
  const bucket = fakeBucket([{ key: 'junk.bin', size: 9 * GB }]);
  const waits = [];

  await worker.scheduled({}, { BUCKET: bucket }, { waitUntil: (p) => waits.push(p) });
  await Promise.all(waits);

  assert.deepEqual(bucket.deleted, ['junk.bin']);
});
