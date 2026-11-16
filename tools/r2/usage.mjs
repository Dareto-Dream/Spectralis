#!/usr/bin/env node
// Measures the release CDN bucket and says whether it is inside its budget. Read-only: it lists
// objects and nothing else. The scheduled workflow runs it so a bucket creeping toward the free
// tier turns into a failed job (and an email) long before it turns into a bill.
//
//   node usage.mjs                 # exit 0 inside budget, 1 over budget, 3 error
//   node usage.mjs --budget-gb 5   # same default as sync.mjs; can never be raised above 8
//
// Environment: R2_ACCOUNT_ID, R2_ACCESS_KEY_ID, R2_SECRET_ACCESS_KEY, optional R2_BUCKET.

import { appendFileSync } from 'node:fs';
import { r2Client, s3Storage } from './s3-storage.mjs';
import { DEFAULT_BUDGET_BYTES, FREE_TIER_BYTES, GB, classify, resolveBudget } from './budget.mjs';

/** Totals a bucket listing by kind. Pure, so it can be tested without a bucket. */
export function summarize(objects, budgetBytes = DEFAULT_BUDGET_BYTES) {
  const budget = resolveBudget(budgetBytes);
  const byKind = {};
  let total = 0;
  for (const o of objects) {
    const kind = classify(o.key).kind;
    byKind[kind] = (byKind[kind] ?? 0) + o.size;
    total += o.size;
  }
  return {
    totalBytes: total,
    budgetBytes: budget,
    freeTierBytes: FREE_TIER_BYTES,
    objects: objects.length,
    byKind,
    ok: total <= budget,
    // A heads-up while there is still room to act on it.
    warn: total > budget * 0.85,
  };
}

const gb = (bytes) => `${(bytes / GB).toFixed(2)} GB`;

async function main() {
  const flag = process.argv.indexOf('--budget-gb');
  const requested = flag >= 0 ? Number(process.argv[flag + 1]) * GB : DEFAULT_BUDGET_BYTES;
  const need = (name) => {
    const value = process.env[name]?.trim();
    if (!value) throw new Error(`${name} is not set.`);
    return value;
  };
  const client = r2Client(need('R2_ACCOUNT_ID'), need('R2_ACCESS_KEY_ID'), need('R2_SECRET_ACCESS_KEY'));
  const bucket = process.env.R2_BUCKET?.trim() || 'spectralis-cdn';
  const usage = summarize(await s3Storage(client, bucket).list(), requested);

  const lines = [
    `${bucket}: ${gb(usage.totalBytes)} in ${usage.objects} objects`,
    `budget ${gb(usage.budgetBytes)}, free tier ${gb(usage.freeTierBytes)}`,
    ...Object.entries(usage.byKind).sort((a, b) => b[1] - a[1]).map(([kind, bytes]) => `  ${kind.padEnd(10)} ${gb(bytes)}`),
    usage.ok ? (usage.warn ? 'WARNING: above 85% of the budget.' : 'Inside the budget.') : 'OVER BUDGET.',
  ];
  for (const line of lines) console.log(line);
  if (process.env.GITHUB_STEP_SUMMARY) {
    appendFileSync(process.env.GITHUB_STEP_SUMMARY, `### Release CDN storage\n\n\`\`\`\n${lines.join('\n')}\n\`\`\`\n`);
  }
  if (usage.warn && usage.ok) console.log('::warning::The release CDN bucket is above 85% of its storage budget.');
  return usage.ok ? 0 : 1;
}

// Only when run directly; the test imports summarize() from this file.
if (/[\\/]usage\.mjs$/.test(process.argv[1] ?? '')) {
  main().then(
    (code) => process.exit(code),
    (error) => {
      console.error(`usage check failed: ${error.message}`);
      process.exit(3);
    },
  );
}
