import test from 'node:test';
import assert from 'node:assert/strict';
import { GB, MAX_BUDGET_BYTES } from './budget.mjs';
import { summarize } from './usage.mjs';

test('usage is totalled by kind and judged against the budget', () => {
  const usage = summarize([
    { key: 'Spectralis-7.1.0-win-x64-full.nupkg', size: 2 * GB },
    { key: 'Spectralis-win-x64-Setup.exe', size: 1 * GB },
    { key: 'releases.win-x64.json', size: 1000 },
    { key: 'visualizers/manifest.json', size: 500 },
  ], 5 * GB);
  assert.equal(usage.totalBytes, 3 * GB + 1500);
  assert.equal(usage.byKind.package, 2 * GB);
  assert.equal(usage.byKind.installer, 1 * GB);
  assert.equal(usage.ok, true);
  assert.equal(usage.warn, false);
});

test('over budget fails, near budget warns, and the budget can not be raised past the hard maximum', () => {
  assert.equal(summarize([{ key: 'a.exe', size: 4.5 * GB }], 5 * GB).warn, true);
  assert.equal(summarize([{ key: 'a.exe', size: 4.5 * GB }], 5 * GB).ok, true);
  assert.equal(summarize([{ key: 'a.exe', size: 5.1 * GB }], 5 * GB).ok, false);
  assert.equal(summarize([], 50 * GB).budgetBytes, MAX_BUDGET_BYTES);
  assert.equal(summarize([{ key: 'a.exe', size: 9 * GB }], 50 * GB).ok, false);
});
