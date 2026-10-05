// Planning for uploading the redeemable visualizers (their manifest, modules, binaries and artwork) to
// `visualizers/` in the release bucket. Pure: no network, no disk. upload-visualizers.mjs does the work.
//
// They count toward the bucket's size like everything else, so the plan is refused if it would not fit in the
// storage budget (see budget.mjs). They are small next to the release packages, but the cap is the cap.

import { formatBytes, resolveBudget } from './budget.mjs';

export const PREFIX = 'visualizers/';
export const MANIFEST = 'manifest.json';

const TYPES = {
  json: 'application/json',
  wat: 'text/plain; charset=utf-8',
  html: 'text/html; charset=utf-8',
  js: 'text/javascript; charset=utf-8',
  png: 'image/png',
  jpg: 'image/jpeg',
  jpeg: 'image/jpeg',
  webp: 'image/webp',
  gif: 'image/gif',
  svg: 'image/svg+xml',
  mp4: 'video/mp4',
  webm: 'video/webm',
};

export function contentType(name) {
  const ext = name.split('.').pop()?.toLowerCase() ?? '';
  return TYPES[ext] ?? 'application/octet-stream';
}

/** The bucket key for a path relative to the source folder, or throws if the path could escape it. */
export function keyFor(relative) {
  const clean = relative.replace(/\\/g, '/').replace(/^\.\//, '');
  const parts = clean.split('/');
  if (!clean || clean.startsWith('/') || /^[A-Za-z]:/.test(clean) || parts.some((p) => p === '' || p === '.' || p === '..')) {
    throw new Error(`"${relative}" is not a path inside the visualizers folder.`);
  }
  return PREFIX + clean;
}

/** Every file a manifest names, relative to the manifest's folder. */
export function manifestRefs(manifest) {
  const refs = new Set();
  for (const entry of manifest?.visualizers ?? []) {
    for (const key of ['moduleUrl', 'binaryUrl', 'htmlUrl']) if (typeof entry[key] === 'string') refs.add(entry[key]);
    for (const group of [entry.dataUrls, entry.assetUrls]) {
      for (const value of Object.values(group ?? {})) if (typeof value === 'string') refs.add(value);
    }
  }
  return [...refs].sort();
}

/**
 * @param {{key:string,size:number}[]} existing  everything already in the bucket
 * @param {{relative:string,size:number,path:string}[]} files  what is on disk under the source folder
 */
export function planVisualizerUpload({ existing, files, budgetBytes }) {
  const budget = resolveBudget(budgetBytes);
  const stored = new Map(existing.map((o) => [o.key, o.size]));
  const wanted = files.map((f) => ({ ...f, key: keyFor(f.relative) }));

  const changed = wanted.filter((f) => stored.get(f.key) !== f.size);
  // The manifest goes last, so it never names a file that is not there yet.
  changed.sort((a, b) => Number(a.relative === MANIFEST) - Number(b.relative === MANIFEST));

  // Overwriting keeps the old object until the new one lands, so count every byte sent on top of what is stored.
  const existingTotal = existing.reduce((n, o) => n + o.size, 0);
  const sendBytes = changed.reduce((n, f) => n + f.size, 0);
  const peak = existingTotal + sendBytes;
  const plan = { upload: changed, skipped: wanted.length - changed.length, sendBytes, peakBytes: peak, budgetBytes: budget };

  if (peak > budget) {
    return {
      ok: false,
      ...plan,
      upload: [],
      reason: `Uploading ${formatBytes(sendBytes)} would take the bucket to ${formatBytes(peak)}, over the ${formatBytes(budget)} budget. Nothing was uploaded.`,
    };
  }
  return { ok: true, ...plan };
}
