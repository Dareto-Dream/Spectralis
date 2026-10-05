#!/usr/bin/env node
// Budget-guarded upload of Velopack release artifacts to the Spectralis CDN bucket on Cloudflare R2.
//
//   node sync.mjs --source ../../releases-velopack            # dry run: shows the plan, writes nothing
//   node sync.mjs --source ../../releases-velopack --apply    # does it
//
// Environment (an R2 API token scoped to the one bucket, "Object Read & Write"):
//   R2_ACCOUNT_ID, R2_ACCESS_KEY_ID, R2_SECRET_ACCESS_KEY, optional R2_BUCKET (default spectralis-cdn)
//
// Flags: --keep <n>      releases to keep on the CDN (default 4)
//        --budget-gb <n> storage budget in GB (default 5, can never be set above 8)
//
// R2 has no hard storage cap, so this is the cap: see budget.mjs for the rules and why they hold.

import { readdir, readFile, stat } from 'node:fs/promises';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { r2Client, s3Storage } from './s3-storage.mjs';
import { DEFAULT_BUDGET_BYTES, DEFAULT_KEEP_VERSIONS, GB, MAX_BUDGET_BYTES, classify } from './budget.mjs';
import { runSync } from './sync-core.mjs';

const here = path.dirname(fileURLToPath(import.meta.url));

function parseArgs(argv) {
  const args = { source: path.resolve(here, '../../releases-velopack'), apply: false, keep: DEFAULT_KEEP_VERSIONS, budgetGb: null };
  for (let i = 0; i < argv.length; i++) {
    const a = argv[i];
    if (a === '--apply') args.apply = true;
    else if (a === '--source') args.source = path.resolve(argv[++i]);
    else if (a === '--keep') args.keep = Number.parseInt(argv[++i], 10);
    else if (a === '--budget-gb') args.budgetGb = Number(argv[++i]);
    else if (a === '--help' || a === '-h') args.help = true;
    else throw new Error(`Unknown argument: ${a}`);
  }
  return args;
}

function requireEnv(name) {
  const value = process.env[name]?.trim();
  if (!value) throw new Error(`${name} is not set. See the header of tools/r2/sync.mjs.`);
  return value;
}

async function localFiles(dir) {
  const files = [];
  for (const entry of await readdir(dir, { withFileTypes: true })) {
    if (!entry.isFile() || classify(entry.name).kind === 'unknown') continue;
    const full = path.join(dir, entry.name);
    files.push({ key: entry.name, size: (await stat(full)).size, path: full });
  }
  return files;
}

async function main() {
  const args = parseArgs(process.argv.slice(2));
  if (args.help) {
    console.log('See the header of tools/r2/sync.mjs.');
    return 0;
  }

  const accountId = requireEnv('R2_ACCOUNT_ID');
  const bucket = process.env.R2_BUCKET?.trim() || 'spectralis-cdn';
  const client = r2Client(accountId, requireEnv('R2_ACCESS_KEY_ID'), requireEnv('R2_SECRET_ACCESS_KEY'));

  const requested = args.budgetGb === null ? DEFAULT_BUDGET_BYTES : args.budgetGb * GB;
  if (requested > MAX_BUDGET_BYTES) {
    console.warn(`Requested budget ${args.budgetGb} GB is above the hard maximum of ${MAX_BUDGET_BYTES / GB} GB; using the maximum.`);
  }

  const files = await localFiles(args.source);
  const result = await runSync({
    storage: s3Storage(client, bucket),
    files,
    readFeed: (key) => readFile(path.join(args.source, key), 'utf8'),
    budgetBytes: requested,
    keepVersions: args.keep,
    apply: args.apply,
    log: (line) => console.log(line),
  });

  return result.ok ? 0 : result.wrote ? 2 : 1;
}

main().then(
  (code) => process.exit(code),
  (error) => {
    console.error(`sync failed: ${error.message}`);
    process.exit(3);
  },
);
