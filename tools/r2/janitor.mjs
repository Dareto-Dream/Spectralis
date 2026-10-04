// Janitor Worker: an independent backstop that runs inside Cloudflare on a schedule.
//
// The upload tool (sync.mjs) enforces the storage budget on every normal write. This exists for everything
// else: a bug in that tool, a manual dashboard upload, a runaway script, a token leaked into the wrong hands.
// Every hour it measures the bucket and, only if it is over the emergency ceiling, removes the least valuable
// objects until it's comfortably under again. It never touches release feeds or installers (deleting those
// would break updates for everyone) nor the newest releases. See planEmergencyPrune in budget.mjs.
//
// Bindings and variables:
//   BUCKET        the R2 bucket to watch (binding)
//   CEILING_BYTES emergency ceiling, clamped to the hard maximum in budget.mjs (default: that maximum)
//   TARGET_BYTES  prune down to this much (default 80% of the ceiling)
//   DRY_RUN       "true" to only log what it would delete

import { MAX_BUDGET_BYTES, formatBytes, planEmergencyPrune } from './budget.mjs';

/** Every object in the bucket, following the listing cursor. */
export async function listAll(bucket) {
  const objects = [];
  let cursor;
  do {
    const page = await bucket.list({ cursor, limit: 1000 });
    for (const o of page.objects) objects.push({ key: o.key, size: o.size, uploaded: o.uploaded });
    cursor = page.truncated ? page.cursor : undefined;
  } while (cursor);
  return objects;
}

export async function runJanitor(bucket, env = {}, log = console.log) {
  const ceiling = Math.min(Number(env.CEILING_BYTES) || MAX_BUDGET_BYTES, MAX_BUDGET_BYTES);
  const target = Math.min(Number(env.TARGET_BYTES) || ceiling * 0.8, ceiling);

  const objects = await listAll(bucket);
  const plan = planEmergencyPrune({ objects, ceilingBytes: ceiling, targetBytes: target, protectNewest: 2, maxDeletes: 200 });

  if (plan.action === 'none') {
    log(`janitor: ${formatBytes(plan.total)} of ${formatBytes(ceiling)} ceiling, nothing to do.`);
    return plan;
  }

  log(`janitor: ${formatBytes(plan.total)} is over the ${formatBytes(ceiling)} ceiling. Removing ${plan.deletes.length} object(s): ${plan.deletes.join(', ')}`);
  if (String(env.DRY_RUN) === 'true') {
    log('janitor: DRY_RUN is on, deleting nothing.');
    return { ...plan, dryRun: true };
  }

  if (plan.deletes.length > 0) await bucket.delete(plan.deletes);
  if (plan.stillOverBy > 0) {
    log(`janitor: ERROR still ${formatBytes(plan.stillOverBy)} over the ceiling and nothing left that is safe to remove. Needs a human.`);
  }
  return plan;
}

export default {
  async scheduled(_event, env, ctx) {
    ctx.waitUntil(runJanitor(env.BUCKET, env));
  },
};
