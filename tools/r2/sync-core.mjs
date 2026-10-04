// The upload run itself, written against a small storage interface so it can be tested without R2.
// sync.mjs wires it to the real bucket. Everything that decides *whether* something may be written lives in
// budget.mjs; this file only carries the plan out in a safe order and double-checks the result.

import { classify, formatBytes, planSync } from './budget.mjs';

/**
 * Feed files list every asset ever released. Once old packages are pruned from the CDN the feed must stop
 * naming them, or a client could be told to fetch a delta that no longer exists.
 * Accepts the Velopack `releases.<rid>.json` shape ({ Assets: [{ Version, FileName, ... }] }).
 */
export function pruneFeed(feedText, keptVersions) {
  const kept = new Set(keptVersions);
  const text = feedText.replace(/^﻿/, '');
  if (!text.trimStart().startsWith('{')) {
    // Legacy RELEASES file: one "<sha1> <file name> <size>" line per package.
    const lines = text.split(/\r?\n/).filter((line) => {
      const name = line.trim().split(/\s+/)[1];
      if (!name) return false;
      const c = classify(name);
      return c.kind !== 'package' || kept.has(c.version);
    });
    return `${lines.join('\n')}\n`;
  }
  const feed = JSON.parse(text);
  if (!Array.isArray(feed.Assets)) return feedText; // some other JSON shape; leave it exactly as it is
  return JSON.stringify({ ...feed, Assets: feed.Assets.filter((a) => kept.has(a.Version)) });
}

/**
 * @typedef {{key:string,size:number}} Obj
 * @typedef {Object} Storage
 * @property {() => Promise<Obj[]>} list                 every object currently in the bucket
 * @property {() => Promise<number>} abortStaleUploads   abort unfinished multipart uploads; returns how many
 * @property {(keys:string[]) => Promise<void>} remove
 * @property {(key:string, body:{path?:string,bytes?:Buffer}, size:number) => Promise<void>} put
 * @property {(key:string) => Promise<number|null>} sizeOf   stored size, or null if it isn't there
 *
 * @param {Object} options
 * @param {Storage} options.storage
 * @param {{key:string,size:number,path:string}[]} options.files  candidates found on disk
 * @param {(feedKey:string) => Promise<string>} options.readFeed  contents of a local feed file
 */
export async function runSync({ storage, files, readFeed, budgetBytes, keepVersions, apply, log = () => {} }) {
  const wanted = files.filter((f) => classify(f.key).kind !== 'unknown');
  const skipped = files.length - wanted.length;
  if (skipped > 0) log(`Ignoring ${skipped} local file(s) that aren't release packages, feeds or installers.`);

  if (apply) {
    const aborted = await storage.abortStaleUploads();
    if (aborted > 0) log(`Aborted ${aborted} unfinished upload(s) left in the bucket.`);
  }

  const existing = await storage.list();
  const plan = planSync({ existing, incoming: wanted.map(({ key, size }) => ({ key, size })), budgetBytes, keepVersions });

  log(`In the bucket now: ${formatBytes(existing.reduce((n, o) => n + o.size, 0))}   budget: ${formatBytes(plan.budgetBytes)}`);
  if (!plan.ok) {
    log(`REFUSED: ${plan.reason}`);
    return { ok: false, reason: plan.reason, plan, wrote: false };
  }

  log(`Keeping releases: ${plan.keptVersions.join(', ')}`);
  log(`Upload (${plan.upload.length}): ${plan.upload.map((o) => o.key).join(', ') || '-'}`);
  log(`Delete before upload (${plan.deleteBefore.length}), after (${plan.deleteAfter.length})`);
  log(`Peak ${formatBytes(plan.peakBytes)}, final ${formatBytes(plan.finalBytes)}, budget ${formatBytes(plan.budgetBytes)}`);

  if (!apply) {
    log('Dry run: nothing written. Re-run with --apply to do this.');
    return { ok: true, plan, wrote: false };
  }

  if (plan.deleteBefore.length) await storage.remove(plan.deleteBefore);

  // Packages first, feeds last: a client that reads the feed must always find the files it names.
  const byKey = new Map(wanted.map((f) => [f.key, f]));
  const ordered = [...plan.upload].sort((a, b) => Number(classify(a.key).kind === 'feed') - Number(classify(b.key).kind === 'feed'));
  for (const item of ordered) {
    const file = byKey.get(item.key);
    if (classify(item.key).kind === 'feed') {
      const bytes = Buffer.from(pruneFeed(await readFeed(item.key), plan.keptVersions), 'utf8');
      await storage.put(item.key, { bytes }, bytes.length);
      await verify(storage, item.key, bytes.length);
    } else {
      log(`Uploading ${item.key} (${formatBytes(file.size)})`);
      await storage.put(item.key, { path: file.path }, file.size);
      await verify(storage, item.key, file.size);
    }
  }

  if (plan.deleteAfter.length) await storage.remove(plan.deleteAfter);

  // Trust nothing: measure the real bucket and complain loudly if it isn't what the plan promised.
  const after = await storage.list();
  const total = after.reduce((n, o) => n + o.size, 0);
  if (total > plan.budgetBytes) {
    const reason = `After the run the bucket holds ${formatBytes(total)}, over the ${formatBytes(plan.budgetBytes)} budget. Investigate before uploading anything else.`;
    log(`OVER BUDGET: ${reason}`);
    return { ok: false, reason, plan, wrote: true, totalAfter: total };
  }

  log(`Done. The bucket now holds ${formatBytes(total)} of ${formatBytes(plan.budgetBytes)}.`);
  return { ok: true, plan, wrote: true, totalAfter: total };
}

async function verify(storage, key, expectedSize) {
  const stored = await storage.sizeOf(key);
  if (stored !== expectedSize) {
    throw new Error(`Upload of ${key} did not verify: expected ${expectedSize} bytes, bucket reports ${stored}.`);
  }
}
