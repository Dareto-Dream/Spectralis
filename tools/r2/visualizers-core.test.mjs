import assert from 'node:assert/strict';
import { test } from 'node:test';
import { GB, MAX_BUDGET_BYTES, classify } from './budget.mjs';
import { MANIFEST, PREFIX, contentType, keyFor, manifestRefs, planVisualizerUpload } from './visualizers-core.mjs';

const MB = 1_000_000;
const file = (relative, size) => ({ relative, size, path: `/src/${relative}` });

test('keys land under visualizers/ and are recognised as site content', () => {
  assert.equal(keyFor('zero/clip.mp4'), `${PREFIX}zero/clip.mp4`);
  assert.equal(keyFor('.\\axylus\\axylus.png'), `${PREFIX}axylus/axylus.png`, 'windows paths work');
  assert.equal(classify(keyFor('manifest.json')).kind, 'content');
});

test('a path that could leave the folder is refused', () => {
  for (const bad of ['../secrets.txt', 'a/../../b', '/etc/passwd', 'C:/Windows/x', '', 'a//b', './']) {
    assert.throws(() => keyFor(bad), /not a path inside/, bad);
  }
});

test('files get a sensible content type, and unknown ones stay generic', () => {
  assert.equal(contentType('a/module.json'), 'application/json');
  assert.equal(contentType('a/v.WAT'), 'text/plain; charset=utf-8');
  assert.equal(contentType('a/clip.mp4'), 'video/mp4');
  assert.equal(contentType('a/art.webp'), 'image/webp');
  assert.equal(contentType('a/mystery.bin'), 'application/octet-stream');
  assert.equal(contentType('noextension'), 'application/octet-stream');
});

test('the files a manifest names are collected from every field that points at one', () => {
  const refs = manifestRefs({
    visualizers: [
      { id: 'a', moduleUrl: 'a/m.json', binaryUrl: 'a/v.wat', dataUrls: { cfg: 'a/cfg.json' }, assetUrls: { cover: 'a/c.png', clip: 'a/c.mp4' } },
      { id: 'b', moduleUrl: 'b/m.json', htmlUrl: 'b/v.html', assetUrls: { cover: 'a/c.png' } },
    ],
  });
  assert.deepEqual(refs, ['a/c.mp4', 'a/c.png', 'a/cfg.json', 'a/m.json', 'a/v.wat', 'b/m.json', 'b/v.html']);
  assert.deepEqual(manifestRefs({}), []);
});

test('everything is uploaded the first time, with the manifest last', () => {
  const files = [file('manifest.json', 3000), file('zero/clip.mp4', 30 * MB), file('808/808.webp', 0.4 * MB)];

  const plan = planVisualizerUpload({ existing: [], files, budgetBytes: 5 * GB });

  assert.equal(plan.ok, true);
  assert.equal(plan.upload.length, 3);
  assert.equal(plan.upload.at(-1).relative, MANIFEST);
  assert.equal(plan.sendBytes, 3000 + 30 * MB + 0.4 * MB);
});

test('files already in the bucket at the same size are skipped, and a changed one is sent again', () => {
  const files = [file('manifest.json', 3000), file('a.png', 100), file('b.png', 200)];
  const existing = [{ key: 'visualizers/manifest.json', size: 3000 }, { key: 'visualizers/a.png', size: 100 }, { key: 'visualizers/b.png', size: 150 }];

  const plan = planVisualizerUpload({ existing, files, budgetBytes: 5 * GB });

  assert.deepEqual(plan.upload.map((f) => f.relative), ['b.png']);
  assert.equal(plan.skipped, 2);
});

test('an upload that would not fit in the budget is refused and sends nothing', () => {
  const existing = [{ key: 'Spectralis-1.0.0-win-x64-full.nupkg', size: 4.95 * GB }];

  const plan = planVisualizerUpload({ existing, files: [file('zero/clip.mp4', 100 * MB)], budgetBytes: 5 * GB });

  assert.equal(plan.ok, false);
  assert.deepEqual(plan.upload, []);
  assert.match(plan.reason, /over the 5\.00 GB budget/);
});

test('the budget can never be raised above the hard maximum', () => {
  const plan = planVisualizerUpload({ existing: [], files: [file('a.png', 1)], budgetBytes: 100 * GB });

  assert.equal(plan.budgetBytes, MAX_BUDGET_BYTES);
});

test('re-running with nothing changed uploads nothing', () => {
  const files = [file('manifest.json', 3000), file('a.png', 100)];
  const existing = files.map((f) => ({ key: keyFor(f.relative), size: f.size }));

  const plan = planVisualizerUpload({ existing, files, budgetBytes: 5 * GB });

  assert.equal(plan.ok, true);
  assert.equal(plan.upload.length, 0);
});
