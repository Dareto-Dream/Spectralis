// Storage budget rules for the Spectralis release CDN bucket.
//
// Cloudflare R2 has no hard storage cap: past the 10 GB free tier it just keeps accepting writes and bills
// for them. So the cap is ours to enforce, and this module is the one place the rules live, pure and tested,
// shared by the upload tool (sync.mjs) and the janitor Worker that runs inside Cloudflare.
//
// Units: Cloudflare's "GB" for billing is decimal, so everything here uses 1 GB = 1,000,000,000 bytes, the
// smaller reading, which makes every limit below conservative whichever way it is counted.

export const GB = 1_000_000_000;

/** What Cloudflare gives away per month. We never plan anywhere near it. */
export const FREE_TIER_BYTES = 10 * GB;

/**
 * No configuration, environment variable or flag can raise a budget above this. It's 80% of the free tier,
 * leaving room for a download that is in flight, rounding, and our own measurement being a little behind.
 */
export const MAX_BUDGET_BYTES = 8 * GB;

/** What the upload tool plans against unless told to use less. Normal use (a handful of releases) is a third of it. */
export const DEFAULT_BUDGET_BYTES = 5 * GB;

/** Releases kept on the CDN; older ones live on the legacy CDN and in the local backup. */
export const DEFAULT_KEEP_VERSIONS = 4;

/** A requested budget in bytes, clamped into (0, MAX_BUDGET_BYTES]. Anything unparseable falls back to the default. */
export function resolveBudget(requestedBytes) {
  const n = Number(requestedBytes);
  if (!Number.isFinite(n) || n <= 0) return DEFAULT_BUDGET_BYTES;
  return Math.min(Math.floor(n), MAX_BUDGET_BYTES);
}

// ── key classification ───────────────────────────────────────────────────────────────────────────

/** Velopack package names: Spectralis-<semver>-<rid>-<full|delta>.nupkg */
const PACKAGE = /^(?<id>[A-Za-z0-9_.]+?)-(?<version>\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?)-(?<rid>[a-z0-9]+-[a-z0-9]+)-(?<type>full|delta)\.nupkg$/;
const FEED = /^(?:releases\.[A-Za-z0-9_-]+\.json|RELEASES(?:-[A-Za-z0-9_-]+)?)$/; // assets.*.json is a build log with local paths: never ours to publish
const INSTALLER = /\.(?:exe|msi|appimage|pkg|dmg)$/i;

/**
 * Classifies a bucket key by its file name (the directory part is ignored), with one exception: everything under
 * `visualizers/` is site content (the redeemable visualizers and their manifest). It is never pruned by version
 * and the janitor never deletes it, but it still counts toward the size of the bucket.
 */
export function classify(key) {
  if (key.startsWith('visualizers/')) return { kind: 'content' };
  const name = key.split('/').pop() ?? key;
  const pkg = PACKAGE.exec(name);
  if (pkg) return { kind: 'package', version: pkg.groups.version, rid: pkg.groups.rid, type: pkg.groups.type };
  if (FEED.test(name)) return { kind: 'feed' };
  if (INSTALLER.test(name)) return { kind: 'installer' };
  return { kind: 'unknown' };
}

/** Compares semver-ish versions: numeric parts first, a pre-release sorts below its release. Returns <0, 0, >0. */
export function compareVersions(a, b) {
  const split = (v) => {
    const [core, pre] = v.split(/-(.+)/);
    return { nums: core.split('.').map((p) => Number.parseInt(p, 10) || 0), pre: pre ?? null };
  };
  const x = split(a);
  const y = split(b);
  for (let i = 0; i < Math.max(x.nums.length, y.nums.length); i++) {
    const d = (x.nums[i] ?? 0) - (y.nums[i] ?? 0);
    if (d !== 0) return d;
  }
  if (x.pre === y.pre) return 0;
  if (x.pre === null) return 1;
  if (y.pre === null) return -1;
  return x.pre < y.pre ? -1 : 1;
}

/** Distinct package versions found in `keys`, newest first. */
export function versionsOf(keys) {
  const set = new Set();
  for (const key of keys) {
    const c = classify(key);
    if (c.kind === 'package') set.add(c.version);
  }
  return [...set].sort((a, b) => compareVersions(b, a));
}

// ── planning an upload ───────────────────────────────────────────────────────────────────────────

const sum = (items) => items.reduce((total, item) => total + item.size, 0);

/**
 * Decides what an upload run may do. Pure: give it what's in the bucket and what we want to put there.
 *
 * Rules, in order of importance:
 *  1. Peak storage (the most the bucket ever holds during the run) and final storage both stay within the
 *     budget. If that's impossible the plan is refused and nothing at all is uploaded.
 *  2. Only the newest `keepVersions` releases are kept; older packages are pruned.
 *  3. Prefer uploading first and pruning after, so clients never see a feed pointing at deleted files; delete
 *     early (oldest versions only, as few as needed) only when the budget demands it.
 *  4. Feed files and installers are never pruned by version, only replaced in place.
 *
 * @param {{key:string,size:number}[]} existing  objects already in the bucket
 * @param {{key:string,size:number}[]} incoming  objects the run wants to write (same key replaces)
 * @returns {{ok:boolean, reason?:string, deleteBefore:string[], upload:{key:string,size:number}[], deleteAfter:string[],
 *            peakBytes:number, finalBytes:number, budgetBytes:number, keptVersions:string[]}}
 */
export function planSync({ existing, incoming, budgetBytes = DEFAULT_BUDGET_BYTES, keepVersions = DEFAULT_KEEP_VERSIONS }) {
  const budget = resolveBudget(budgetBytes);
  const keep = Math.max(1, Math.floor(keepVersions));

  const incomingByKey = new Map(incoming.map((o) => [o.key, o]));
  const world = new Map(existing.map((o) => [o.key, o]));
  for (const o of incoming) world.set(o.key, o);

  const keptVersions = versionsOf([...world.keys()]).slice(0, keep);
  const kept = new Set(keptVersions);

  const prunable = [...world.values()]
    .filter((o) => {
      const c = classify(o.key);
      return c.kind === 'package' && !kept.has(c.version);
    })
    // Oldest version first: those are the first to go if we must delete early.
    .sort((a, b) => compareVersions(classify(a.key).version, classify(b.key).version));
  const prunableKeys = new Set(prunable.map((o) => o.key));

  // Never upload something we'd immediately prune.
  const upload = incoming.filter((o) => !prunableKeys.has(o.key));
  const uploadKeys = new Set(upload.map((o) => o.key));

  const existingTotal = sum(existing);
  const finalState = [...world.values()].filter((o) => !prunableKeys.has(o.key));
  const finalBytes = sum(finalState);

  const refuse = (reason, peak = finalBytes) => ({
    ok: false, reason, deleteBefore: [], upload: [], deleteAfter: [],
    peakBytes: peak, finalBytes, budgetBytes: budget, keptVersions,
  });

  if (finalBytes > budget) {
    return refuse(
      `Even keeping only the newest ${keep} release(s) the bucket would hold ${fmt(finalBytes)}, over the ${fmt(budget)} budget. Nothing was uploaded.`,
    );
  }

  // Peak if we upload first and prune afterwards. Overwriting a key keeps the old object until the new one
  // finishes, and uploads may overlap, so count every uploaded byte on top of everything already stored.
  // That's the safe upper bound, even if it overstates the peak a little when a key is replaced.
  const uploadBytes = sum(upload);
  const uploadFirstPeak = existingTotal + uploadBytes;

  if (uploadFirstPeak <= budget) {
    return {
      ok: true, deleteBefore: [], upload,
      deleteAfter: prunable.map((o) => o.key).filter((k) => existing.some((e) => e.key === k)),
      peakBytes: uploadFirstPeak, finalBytes, budgetBytes: budget, keptVersions,
    };
  }

  // Not enough room to upload first: delete the oldest prunable objects, as few as it takes, then upload.
  const deleteBefore = [];
  const storedSize = new Map(existing.map((e) => [e.key, e.size])); // what is really in the bucket, not what we'd replace it with
  let current = existingTotal;
  for (const o of prunable) {
    if (!storedSize.has(o.key)) continue;
    if (current + uploadBytes <= budget) break;
    deleteBefore.push(o.key);
    current -= storedSize.get(o.key);
  }
  const peak = current + uploadBytes;
  if (peak > budget) {
    return refuse(
      `Uploading would peak at ${fmt(peak)} even after removing every old release, over the ${fmt(budget)} budget. Nothing was uploaded.`,
      peak,
    );
  }

  return {
    ok: true, deleteBefore, upload,
    deleteAfter: prunable.map((o) => o.key).filter((k) => existing.some((e) => e.key === k) && !deleteBefore.includes(k)),
    peakBytes: peak, finalBytes, budgetBytes: budget, keptVersions,
  };
}

// ── the janitor's emergency pruning ──────────────────────────────────────────────────────────────

/**
 * What the janitor Worker may delete when the bucket is over `ceilingBytes` for any reason (a bug, a manual
 * upload, a runaway script). It deliberately does as little as possible, in this order:
 *   1. objects we don't recognise at all (not ours), oldest first
 *   2. old release packages, oldest version first, never the newest `protectNewest` versions
 * Feed files and installers are never touched: deleting those would break updates for everyone.
 * It stops as soon as the total is back under `targetBytes` and never exceeds `maxDeletes` per run.
 *
 * @param {{key:string,size:number,uploaded?:number|string|Date}[]} objects
 */
export function planEmergencyPrune({ objects, ceilingBytes, targetBytes, protectNewest = 2, maxDeletes = 200 }) {
  const ceiling = Math.min(Math.max(1, Number(ceilingBytes) || MAX_BUDGET_BYTES), MAX_BUDGET_BYTES);
  const target = Math.min(Math.max(0, Number(targetBytes) || ceiling * 0.8), ceiling);

  const total = sum(objects);
  if (total <= ceiling) return { action: 'none', total, deletes: [], stillOverBy: 0 };

  const protectedVersions = new Set(versionsOf(objects.map((o) => o.key)).slice(0, Math.max(0, protectNewest)));
  const stamp = (o) => (o.uploaded ? new Date(o.uploaded).getTime() : 0);

  const unknown = objects.filter((o) => classify(o.key).kind === 'unknown').sort((a, b) => stamp(a) - stamp(b));
  const oldPackages = objects
    .filter((o) => {
      const c = classify(o.key);
      return c.kind === 'package' && !protectedVersions.has(c.version);
    })
    .sort((a, b) => compareVersions(classify(a.key).version, classify(b.key).version) || stamp(a) - stamp(b));

  const deletes = [];
  let remaining = total;
  for (const o of [...unknown, ...oldPackages]) {
    if (remaining <= target || deletes.length >= maxDeletes) break;
    deletes.push(o.key);
    remaining -= o.size;
  }

  return { action: 'prune', total, deletes, stillOverBy: Math.max(0, remaining - ceiling), remaining };
}

function fmt(bytes) {
  return `${(bytes / GB).toFixed(2)} GB`;
}

export { fmt as formatBytes };
