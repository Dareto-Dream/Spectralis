#!/usr/bin/env node
// Deploys janitor.mjs (+ budget.mjs) as a cron-triggered Worker through the Cloudflare API.
//
//   CLOUDFLARE_API_TOKEN=... CLOUDFLARE_ACCOUNT_ID=... node deploy-janitor.mjs
//
// Options (flags): --bucket <name>   bucket to watch            (default spectralis-cdn)
//                  --name <worker>   Worker script name         (default spectralis-janitor)
//                  --cron "<expr>"   schedule                   (default "17 * * * *", hourly)
//                  --ceiling <bytes> emergency ceiling          (default: the hard maximum in budget.mjs)
//                  --target <bytes>  prune down to this         (default: 80% of ceiling)
//                  --dry-run-worker  deploy with DRY_RUN=true so it only logs
//                  --delete          remove the Worker instead (used to clean up tests)

import { readFile } from 'node:fs/promises';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const here = path.dirname(fileURLToPath(import.meta.url));
const args = { bucket: 'spectralis-cdn', name: 'spectralis-janitor', cron: '17 * * * *' };
const argv = process.argv.slice(2);
for (let i = 0; i < argv.length; i++) {
  const a = argv[i];
  if (a === '--bucket') args.bucket = argv[++i];
  else if (a === '--name') args.name = argv[++i];
  else if (a === '--cron') args.cron = argv[++i];
  else if (a === '--ceiling') args.ceiling = argv[++i];
  else if (a === '--target') args.target = argv[++i];
  else if (a === '--dry-run-worker') args.dryRun = true;
  else if (a === '--delete') args.remove = true;
  else throw new Error(`Unknown argument: ${a}`);
}

const token = process.env.CLOUDFLARE_API_TOKEN;
const account = process.env.CLOUDFLARE_ACCOUNT_ID;
if (!token || !account) throw new Error('Set CLOUDFLARE_API_TOKEN and CLOUDFLARE_ACCOUNT_ID.');

const api = `https://api.cloudflare.com/client/v4/accounts/${account}/workers/scripts/${args.name}`;
const call = async (url, init) => {
  const res = await fetch(url, { ...init, headers: { Authorization: `Bearer ${token}`, ...init.headers } });
  const body = await res.json().catch(() => ({}));
  if (!res.ok || body.success === false) throw new Error(`${init.method} ${url.split('/accounts/')[1]} failed: ${JSON.stringify(body.errors ?? body)}`);
  return body;
};

if (args.remove) {
  await call(api, { method: 'DELETE', headers: {} });
  console.log(`Deleted Worker ${args.name}.`);
  process.exit(0);
}

const bindings = [{ type: 'r2_bucket', name: 'BUCKET', bucket_name: args.bucket }];
if (args.ceiling) bindings.push({ type: 'plain_text', name: 'CEILING_BYTES', text: String(args.ceiling) });
if (args.target) bindings.push({ type: 'plain_text', name: 'TARGET_BYTES', text: String(args.target) });
if (args.dryRun) bindings.push({ type: 'plain_text', name: 'DRY_RUN', text: 'true' });

const form = new FormData();
form.set('metadata', new Blob([JSON.stringify({ main_module: 'janitor.mjs', compatibility_date: '2026-09-01', bindings })], { type: 'application/json' }));
for (const file of ['janitor.mjs', 'budget.mjs']) {
  form.set(file, new Blob([await readFile(path.join(here, file))], { type: 'application/javascript+module' }), file);
}

await call(api, { method: 'PUT', body: form, headers: {} });
await call(`${api}/schedules`, { method: 'PUT', body: JSON.stringify([{ cron: args.cron }]), headers: { 'Content-Type': 'application/json' } });
console.log(`Deployed ${args.name} watching ${args.bucket}, cron "${args.cron}".`);
