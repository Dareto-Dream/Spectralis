import assert from 'node:assert/strict';
import { test } from 'node:test';
import {
  DEFAULT_BUDGET_BYTES, GB, MAX_BUDGET_BYTES, FREE_TIER_BYTES,
  classify, compareVersions, planEmergencyPrune, planSync, resolveBudget, versionsOf,
} from './budget.mjs';

const MB = 1_000_000;
const pkg = (version, rid, type, size) => ({ key: `Spectralis-${version}-${rid}-${type}.nupkg`, size });
const release = (version, { win = 350 * MB, linux = 285 * MB, delta = 5 * MB } = {}) => [
  pkg(version, 'win-x64', 'full', win),
  pkg(version, 'win-x64', 'delta', delta),
  pkg(version, 'linux-x64', 'full', linux),
  pkg(version, 'linux-x64', 'delta', delta),
];
const feeds = () => [
  { key: 'releases.win-x64.json', size: 10_000 },
  { key: 'releases.linux-x64.json', size: 7_000 },
  { key: 'RELEASES', size: 8_000 },
];
const total = (items) => items.reduce((n, o) => n + o.size, 0);

// ── the limits themselves ────────────────────────────────────────────────────────────────────────

test('the hard maximum is well under the free tier, and the default is under the maximum', () => {
  assert.ok(MAX_BUDGET_BYTES <= FREE_TIER_BYTES * 0.8);
  assert.ok(DEFAULT_BUDGET_BYTES < MAX_BUDGET_BYTES);
});

test('no requested budget can exceed the hard maximum', () => {
  for (const asked of [9 * GB, 10 * GB, 11 * GB, 1e15, Number.MAX_SAFE_INTEGER, Infinity]) {
    const got = resolveBudget(asked);
    assert.ok(got <= MAX_BUDGET_BYTES, `${asked} resolved to ${got}`);
  }
  assert.equal(resolveBudget(2 * GB), 2 * GB);
});

test('nonsense budgets fall back to the safe default instead of disabling the check', () => {
  for (const bad of [0, -5, NaN, undefined, null, 'lots', {}]) {
    assert.equal(resolveBudget(bad), DEFAULT_BUDGET_BYTES);
  }
});

// ── classification + versions ────────────────────────────────────────────────────────────────────

test('keys are classified by file name, whatever directory they sit in', () => {
  assert.deepEqual(classify('Spectralis-6.1.0-win-x64-full.nupkg'), { kind: 'package', version: '6.1.0', rid: 'win-x64', type: 'full' });
  assert.equal(classify('spectralis/Spectralis-5.3.4-linux-x64-delta.nupkg').type, 'delta');
  assert.equal(classify('Spectralis-7.0.0-beta.2-osx-arm64-full.nupkg').version, '7.0.0-beta.2');
  for (const feed of ['releases.win-x64.json', 'releases.linux-x64.json', 'RELEASES', 'RELEASES-linux-x64']) {
    assert.equal(classify(feed).kind, 'feed', feed);
  }
  assert.equal(classify('Spectralis-win-x64-Setup.exe').kind, 'installer');
  assert.equal(classify('Spectralis-linux-x64.AppImage').kind, 'installer');
  assert.equal(classify('random-upload.bin').kind, 'unknown');
  assert.equal(classify('notes/readme.txt').kind, 'unknown');
});

test('versions compare numerically, with pre-releases below their release', () => {
  assert.ok(compareVersions('5.10.0', '5.9.9') > 0);
  assert.ok(compareVersions('6.0.0', '5.99.99') > 0);
  assert.ok(compareVersions('6.0.0', '6.0.0-beta.1') > 0);
  assert.ok(compareVersions('6.0.0-beta.2', '6.0.0-beta.1') > 0);
  assert.equal(compareVersions('1.2.3', '1.2.3'), 0);
});

test('versionsOf lists distinct package versions newest first', () => {
  const keys = [...release('5.4.2'), ...release('6.1.0'), ...release('5.10.0'), ...feeds()].map((o) => o.key);
  assert.deepEqual(versionsOf(keys), ['6.1.0', '5.10.0', '5.4.2']);
});

// ── planning ─────────────────────────────────────────────────────────────────────────────────────

test('a normal release uploads first and prunes old versions after', () => {
  const existing = [...['5.4.2', '6.0.0', '6.0.1', '6.1.0'].flatMap((v) => release(v)), ...feeds()];
  const incoming = [...release('6.2.0'), ...feeds()];

  const plan = planSync({ existing, incoming, keepVersions: 4 });

  assert.equal(plan.ok, true);
  assert.deepEqual(plan.keptVersions, ['6.2.0', '6.1.0', '6.0.1', '6.0.0']);
  assert.deepEqual(plan.deleteBefore, []);
  assert.ok(plan.deleteAfter.every((k) => k.includes('5.4.2')));
  assert.equal(plan.deleteAfter.length, 4);
  assert.ok(plan.peakBytes <= plan.budgetBytes);
  assert.ok(plan.finalBytes < plan.peakBytes);
});

test('asking to mirror every release never uploads the ones that would be pruned', () => {
  const everything = Array.from({ length: 20 }, (_, i) => release(`5.${i}.0`)).flat();
  const plan = planSync({ existing: [], incoming: [...everything, ...feeds()], keepVersions: 4 });

  assert.equal(plan.ok, true);
  const uploadedVersions = new Set(plan.upload.map((o) => classify(o.key).version).filter(Boolean));
  assert.equal(uploadedVersions.size, 4);
  assert.ok(total(plan.upload) < 3 * GB, `uploaded ${total(plan.upload)} bytes`);
  assert.ok(plan.finalBytes <= plan.budgetBytes);
});

test('a tight budget deletes the oldest versions first, and only as many as needed', () => {
  const existing = ['5.0.0', '5.1.0', '5.2.0', '5.3.0'].flatMap((v) => release(v));
  const incoming = release('5.4.0');
  const budget = total(existing) + 100 * MB; // not enough to upload first

  const plan = planSync({ existing, incoming, budgetBytes: budget, keepVersions: 4 });

  assert.equal(plan.ok, true);
  assert.ok(plan.deleteBefore.length > 0);
  assert.ok(plan.deleteBefore.every((k) => k.includes('5.0.0')), 'only the oldest version should go early');
  assert.ok(plan.peakBytes <= budget);
});

test('an upload that cannot fit is refused outright and does nothing', () => {
  const plan = planSync({ existing: [], incoming: release('6.0.0'), budgetBytes: 100 * MB });

  assert.equal(plan.ok, false);
  assert.match(plan.reason, /Nothing was uploaded/);
  assert.deepEqual(plan.upload, []);
  assert.deepEqual(plan.deleteBefore, []);
  assert.deepEqual(plan.deleteAfter, []);
});

test('a budget above the maximum is clamped, not honoured', () => {
  const plan = planSync({ existing: [], incoming: release('6.0.0'), budgetBytes: 50 * GB });

  assert.equal(plan.budgetBytes, MAX_BUDGET_BYTES);
});

test('feeds and installers are never pruned by version', () => {
  const existing = [...release('5.0.0'), ...release('6.0.0'), ...feeds(), { key: 'Spectralis-win-x64-Setup.exe', size: 200 * MB }];
  const plan = planSync({ existing, incoming: release('7.0.0'), keepVersions: 1 });

  for (const key of [...plan.deleteBefore, ...plan.deleteAfter]) {
    assert.equal(classify(key).kind, 'package', `${key} should not be pruned`);
  }
});

test('a replaced key counts its full size on top of the old one while it uploads', () => {
  const existing = [{ key: 'releases.win-x64.json', size: 1 * GB }];
  const incoming = [{ key: 'releases.win-x64.json', size: 1 * GB }];

  const plan = planSync({ existing, incoming, budgetBytes: 1.5 * GB });

  // Old + new coexist briefly (2 GB) so a 1.5 GB budget can't allow it.
  assert.equal(plan.ok, false);
});

test('overwriting a package re-uploads it but never deletes the version being kept', () => {
  const existing = release('6.1.0');
  const plan = planSync({ existing, incoming: release('6.1.0'), keepVersions: 4 });

  assert.equal(plan.ok, true);
  assert.deepEqual(plan.deleteBefore, []);
  assert.deepEqual(plan.deleteAfter, []);
});

// ── the guarantee, checked against randomly generated buckets ───────────────────────────────────

function mulberry32(seed) {
  return () => {
    seed |= 0; seed = (seed + 0x6d2b79f5) | 0;
    let t = Math.imul(seed ^ (seed >>> 15), 1 | seed);
    t = (t + Math.imul(t ^ (t >>> 7), 61 | t)) ^ t;
    return ((t ^ (t >>> 14)) >>> 0) / 4294967296;
  };
}

test('property: any plan that says ok really stays within budget at its peak and at the end', () => {
  const rand = mulberry32(20261004);
  let ok = 0;
  let refused = 0;

  for (let round = 0; round < 600; round++) {
    const versionsExisting = Array.from({ length: Math.floor(rand() * 12) }, (_, i) => `${5 + Math.floor(i / 4)}.${i % 4}.${Math.floor(rand() * 3)}`);
    const sizeOf = () => Math.floor(rand() * 900 * MB) + 1;
    const existing = [
      ...new Map(
        versionsExisting.flatMap((v) => release(v, { win: sizeOf(), linux: sizeOf(), delta: sizeOf() / 50 })).map((o) => [o.key, o]),
      ).values(),
      ...(rand() < 0.5 ? feeds() : []),
      ...(rand() < 0.3 ? [{ key: 'stray.bin', size: sizeOf() }] : []),
    ];
    const incomingVersions = Array.from({ length: 1 + Math.floor(rand() * 3) }, () => `${6 + Math.floor(rand() * 3)}.${Math.floor(rand() * 5)}.0`);
    const incoming = [
      ...new Map(
        incomingVersions.flatMap((v) => release(v, { win: sizeOf(), linux: sizeOf(), delta: sizeOf() / 50 })).map((o) => [o.key, o]),
      ).values(),
      ...feeds(),
    ];
    const budgetBytes = Math.floor(rand() * 12 * GB); // deliberately includes budgets above the maximum
    const keepVersions = 1 + Math.floor(rand() * 6);

    const plan = planSync({ existing, incoming, budgetBytes, keepVersions });
    const budget = resolveBudget(budgetBytes);

    assert.ok(plan.budgetBytes <= MAX_BUDGET_BYTES);
    assert.equal(plan.budgetBytes, budget);

    if (!plan.ok) {
      refused++;
      assert.deepEqual(plan.upload, [], 'a refused plan uploads nothing');
      assert.deepEqual([...plan.deleteBefore, ...plan.deleteAfter], [], 'a refused plan deletes nothing');
      continue;
    }
    ok++;

    // Independent simulation: replay the plan against a model of the bucket and track the worst moment.
    const bucket = new Map(existing.map((o) => [o.key, o.size]));
    for (const key of plan.deleteBefore) bucket.delete(key);
    const afterEarlyDeletes = [...bucket.values()].reduce((n, s) => n + s, 0);
    const simulatedPeak = afterEarlyDeletes + plan.upload.reduce((n, o) => n + o.size, 0); // every upload in flight at once
    for (const o of plan.upload) bucket.set(o.key, o.size);
    for (const key of plan.deleteAfter) bucket.delete(key);
    const simulatedFinal = [...bucket.values()].reduce((n, s) => n + s, 0);

    assert.ok(simulatedPeak <= budget, `round ${round}: peak ${simulatedPeak} > budget ${budget}`);
    assert.ok(simulatedFinal <= budget, `round ${round}: final ${simulatedFinal} > budget ${budget}`);
    assert.ok(simulatedPeak <= MAX_BUDGET_BYTES && simulatedPeak < FREE_TIER_BYTES);
    assert.ok(simulatedPeak <= plan.peakBytes + 1, 'the plan must not understate its own peak');
    assert.ok(simulatedFinal <= plan.finalBytes + 1);

    const touched = [...plan.deleteBefore, ...plan.deleteAfter];
    assert.ok(touched.every((k) => classify(k).kind === 'package'), 'only packages are ever pruned');
    assert.ok(plan.keptVersions.length <= keepVersions);
    const uploadKeys = new Set(plan.upload.map((o) => o.key));
    assert.ok(touched.every((k) => !uploadKeys.has(k)), 'nothing is both uploaded and deleted');
  }

  assert.ok(ok > 50 && refused > 50, `the generator should exercise both outcomes (ok ${ok}, refused ${refused})`);
});

// ── emergency pruning (the janitor) ──────────────────────────────────────────────────────────────

const stamped = (items, start = Date.parse('2026-01-01')) => items.map((o, i) => ({ ...o, uploaded: new Date(start + i * 1000).toISOString() }));

test('under the ceiling the janitor does nothing', () => {
  const plan = planEmergencyPrune({ objects: stamped([...release('6.0.0'), ...feeds()]), ceilingBytes: 5 * GB });

  assert.equal(plan.action, 'none');
  assert.deepEqual(plan.deletes, []);
});

test('over the ceiling it removes unknown junk first, oldest first', () => {
  const objects = stamped([
    ...release('6.0.0'), ...feeds(),
    { key: 'junk-a.bin', size: 2 * GB }, { key: 'junk-b.bin', size: 2 * GB },
  ]);

  const plan = planEmergencyPrune({ objects, ceilingBytes: 3 * GB, targetBytes: 2.5 * GB });

  assert.equal(plan.action, 'prune');
  assert.deepEqual(plan.deletes, ['junk-a.bin', 'junk-b.bin']);
  assert.ok(plan.remaining < 3 * GB);
});

test('it then removes old versions but protects the newest ones, the feeds and the installers', () => {
  const objects = stamped([
    ...['5.0.0', '5.1.0', '5.2.0', '6.0.0'].flatMap((v) => release(v, { win: 900 * MB, linux: 900 * MB })),
    ...feeds(),
    { key: 'Spectralis-win-x64-Setup.exe', size: 300 * MB },
  ]);

  const plan = planEmergencyPrune({ objects, ceilingBytes: 4 * GB, targetBytes: 3 * GB, protectNewest: 2 });

  assert.equal(plan.action, 'prune');
  assert.ok(plan.deletes.length > 0);
  for (const key of plan.deletes) {
    const c = classify(key);
    assert.equal(c.kind, 'package');
    assert.ok(!['6.0.0', '5.2.0'].includes(c.version), `${key} belongs to a protected version`);
  }
  // Oldest version goes first.
  assert.ok(plan.deletes[0].includes('5.0.0'));
});

test('it reports when it cannot get under the ceiling without touching protected files', () => {
  const objects = stamped([...release('6.0.0', { win: 4 * GB, linux: 4 * GB }), ...feeds()]);

  const plan = planEmergencyPrune({ objects, ceilingBytes: 5 * GB, protectNewest: 2 });

  assert.deepEqual(plan.deletes, []);
  assert.ok(plan.stillOverBy > 0);
});

test('it never deletes more than the per-run cap', () => {
  const junk = Array.from({ length: 500 }, (_, i) => ({ key: `junk-${i}.bin`, size: 50 * MB }));

  const plan = planEmergencyPrune({ objects: stamped(junk), ceilingBytes: 1 * GB, targetBytes: 0.5 * GB, maxDeletes: 50 });

  assert.equal(plan.deletes.length, 50);
});

test('the janitor ceiling cannot be configured above the hard maximum either', () => {
  const objects = stamped([{ key: 'junk.bin', size: 9 * GB }]);

  const plan = planEmergencyPrune({ objects, ceilingBytes: 50 * GB });

  assert.equal(plan.action, 'prune', 'a 9 GB bucket must trigger even if someone asked for a 50 GB ceiling');
});

test('the visualizers are site content: counted, never pruned by version, never deleted by the janitor', () => {
  assert.equal(classify('visualizers/zero/clip_twerk.mp4').kind, 'content');
  assert.equal(classify('visualizers/manifest.json').kind, 'content');
  assert.equal(classify('manifest.json').kind, 'unknown', 'only the visualizers folder is content');

  const content = [{ key: 'visualizers/zero/clip.mp4', size: 40 * MB }, { key: 'visualizers/manifest.json', size: 3000 }];
  const packages = [
    { key: 'Spectralis-1.0.0-win-x64-full.nupkg', size: 900 * MB },
    { key: 'Spectralis-2.0.0-win-x64-full.nupkg', size: 900 * MB },
    { key: 'Spectralis-3.0.0-win-x64-full.nupkg', size: 900 * MB },
  ];
  const sync = planSync({ existing: [...content, ...packages], incoming: [], budgetBytes: 5 * GB, keepVersions: 1 });
  assert.ok(sync.deleteAfter.every((k) => !k.startsWith('visualizers/')), 'a sync never prunes them');

  const prune = planEmergencyPrune({ objects: [...content, ...packages], ceilingBytes: 1 * GB, targetBytes: 0.5 * GB, protectNewest: 1 });
  assert.ok(prune.deletes.every((k) => !k.startsWith('visualizers/')), 'the janitor never deletes them');
  assert.ok(prune.total >= 2700 * MB + 40 * MB, 'but they are counted in the total');
});
