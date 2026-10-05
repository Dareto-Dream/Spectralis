#!/usr/bin/env node
// Uploads the redeemable visualizers to `visualizers/` in the release bucket, served at
// https://spectralis-cdn.deltavdevs.com/visualizers/manifest.json (what the app redeems keys against).
//
//   node upload-visualizers.mjs --source <folder with manifest.json>            # dry run: shows the plan
//   node upload-visualizers.mjs --source <folder with manifest.json> --apply
//
// Environment: R2_ACCOUNT_ID, R2_ACCESS_KEY_ID, R2_SECRET_ACCESS_KEY, optional R2_BUCKET (default spectralis-cdn).
// Flags: --budget-gb <n>   storage budget in GB (default 5, never above 8). The upload is refused if it won't fit.

import { readdir, readFile, stat } from 'node:fs/promises';
import path from 'node:path';
import { DEFAULT_BUDGET_BYTES, GB, MAX_BUDGET_BYTES, formatBytes } from './budget.mjs';
import { r2Client, s3Storage } from './s3-storage.mjs';
import { MANIFEST, contentType, manifestRefs, planVisualizerUpload } from './visualizers-core.mjs';

const args = { source: null, apply: false, budgetGb: null };
const argv = process.argv.slice(2);
for (let i = 0; i < argv.length; i++) {
  if (argv[i] === '--source') args.source = path.resolve(argv[++i]);
  else if (argv[i] === '--apply') args.apply = true;
  else if (argv[i] === '--budget-gb') args.budgetGb = Number(argv[++i]);
  else throw new Error(`Unknown argument: ${argv[i]}`);
}
if (!args.source) throw new Error('Pass --source <folder with manifest.json>.');

const env = name => {
  const value = process.env[name]?.trim();
  if (!value) throw new Error(`${name} is not set.`);
  return value;
};

async function walk(dir, base = dir) {
  const files = [];
  for (const entry of await readdir(dir, { withFileTypes: true })) {
    const full = path.join(dir, entry.name);
    if (entry.isDirectory()) files.push(...await walk(full, base));
    else if (entry.isFile()) files.push({ relative: path.relative(base, full).split(path.sep).join('/'), size: (await stat(full)).size, path: full });
  }
  return files;
}

const manifest = JSON.parse((await readFile(path.join(args.source, MANIFEST), 'utf8')).replace(/^﻿/, ''));
if (!Array.isArray(manifest.visualizers)) throw new Error('manifest.json has no "visualizers" list.');

const files = await walk(args.source);
const onDisk = new Set(files.map(f => f.relative));
const missing = manifestRefs(manifest).filter(ref => !onDisk.has(ref));
for (const ref of missing) console.warn(`warning: the manifest names ${ref} but it is not in the source folder`);

const client = r2Client(env('R2_ACCOUNT_ID'), env('R2_ACCESS_KEY_ID'), env('R2_SECRET_ACCESS_KEY'));
const storage = s3Storage(client, process.env.R2_BUCKET?.trim() || 'spectralis-cdn');

const requested = args.budgetGb === null ? DEFAULT_BUDGET_BYTES : args.budgetGb * GB;
if (requested > MAX_BUDGET_BYTES) console.warn(`Requested budget is above the hard maximum of ${MAX_BUDGET_BYTES / GB} GB; using the maximum.`);

const existing = await storage.list();
const plan = planVisualizerUpload({ existing, files, budgetBytes: requested });
console.log(`In the bucket now: ${formatBytes(existing.reduce((n, o) => n + o.size, 0))}   budget: ${formatBytes(plan.budgetBytes)}`);
if (!plan.ok) {
  console.error(`REFUSED: ${plan.reason}`);
  process.exit(1);
}
console.log(`Upload ${plan.upload.length} file(s), ${formatBytes(plan.sendBytes)}; skipping ${plan.skipped} already there. Peak ${formatBytes(plan.peakBytes)}.`);
if (!args.apply) {
  console.log('Dry run: nothing written. Re-run with --apply to do this.');
  process.exit(0);
}

for (const item of plan.upload) {
  await storage.put(item.key, { path: item.path }, item.size, contentType(item.relative));
  const stored = await storage.sizeOf(item.key);
  if (stored !== item.size) throw new Error(`Upload of ${item.key} did not verify: expected ${item.size} bytes, the bucket reports ${stored}.`);
}

const total = (await storage.list()).reduce((n, o) => n + o.size, 0);
if (total > plan.budgetBytes) {
  console.error(`OVER BUDGET: the bucket holds ${formatBytes(total)}, over the ${formatBytes(plan.budgetBytes)} budget.`);
  process.exit(2);
}
console.log(`Done. The bucket now holds ${formatBytes(total)} of ${formatBytes(plan.budgetBytes)}.`);
