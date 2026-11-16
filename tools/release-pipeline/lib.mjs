// The rules of the release pipeline, with no I/O so they can be tested: which version comes next,
// whether a merge should ship the app at all, and how a pull request description becomes a
// changelog entry and a row in the release index. The workflows call these through cli.mjs.

const STABLE = /^v?(\d+)\.(\d+)\.(\d+)$/;
const ANY = /^v?(\d+)\.(\d+)\.(\d+)(?:-([0-9A-Za-z.-]+))?$/;

/** Parses "7.1.0", "v7.1.0" or "7.1.1-beta.4". Returns null for anything else. */
export function parseVersion(text) {
  const m = ANY.exec(String(text ?? '').trim());
  if (!m) return null;
  return { major: +m[1], minor: +m[2], patch: +m[3], pre: m[4] ?? null };
}

/** Semver ordering: numeric parts first, a pre-release sorts below its release, pre-release ids compare part by part. */
export function compareVersions(a, b) {
  const x = parseVersion(a);
  const y = parseVersion(b);
  if (!x || !y) return String(a).localeCompare(String(b));
  for (const k of ['major', 'minor', 'patch']) {
    if (x[k] !== y[k]) return x[k] - y[k];
  }
  if (x.pre === y.pre) return 0;
  if (x.pre === null) return 1;
  if (y.pre === null) return -1;
  const xs = x.pre.split('.');
  const ys = y.pre.split('.');
  for (let i = 0; i < Math.max(xs.length, ys.length); i++) {
    if (xs[i] === undefined) return -1;
    if (ys[i] === undefined) return 1;
    const xn = /^\d+$/.test(xs[i]);
    const yn = /^\d+$/.test(ys[i]);
    if (xn && yn) {
      if (+xs[i] !== +ys[i]) return +xs[i] - +ys[i];
    } else if (xn !== yn) {
      return xn ? -1 : 1;
    } else if (xs[i] !== ys[i]) {
      return xs[i] < ys[i] ? -1 : 1;
    }
  }
  return 0;
}

/** The newest stable version among tags, feed versions and whatever else is passed in. */
export function latestStable(candidates) {
  const stable = candidates.map((c) => String(c ?? '').trim()).filter((c) => STABLE.test(c)).map((c) => c.replace(/^v/, ''));
  if (stable.length === 0) return null;
  return stable.sort(compareVersions).at(-1);
}

/** Versions listed in a Velopack releases.<rid>.json feed. */
export function versionsInFeed(feedJson) {
  try {
    const feed = typeof feedJson === 'string' ? JSON.parse(feedJson) : feedJson;
    return (feed?.Assets ?? []).map((a) => a?.Version).filter(Boolean);
  } catch {
    return [];
  }
}

export function bump(version, kind = 'patch') {
  const v = parseVersion(version);
  if (!v) throw new Error(`"${version}" is not a version.`);
  if (kind === 'major') return `${v.major + 1}.0.0`;
  if (kind === 'minor') return `${v.major}.${v.minor + 1}.0`;
  return `${v.major}.${v.minor}.${v.patch + 1}`;
}

export function bumpKind(labels = []) {
  const names = labels.map((l) => String(l).toLowerCase());
  if (names.includes('release:major')) return 'major';
  if (names.includes('release:minor')) return 'minor';
  return 'patch';
}

/**
 * A version the PR title opens with ("v7.2.0 — the big one", "Release 8.0.0") wins over the label
 * bump. Only the start counts, so a title that merely mentions a number is not a version request.
 */
export function explicitVersion(title) {
  const m = /^\s*(?:release\s+)?v?(\d+\.\d+\.\d+)(?=$|[\s:,—–-])/i.exec(String(title ?? ''));
  return m ? m[1] : null;
}

// Only these paths end up inside the installer. A merge that touches nothing here (site copy, docs,
// the backend, CI itself) deploys whatever it deploys but does not cut an app release.
const APP_PATHS = [
  'Spectralis.App/', 'Spectralis.Core/', 'Spectralis.Installer/', 'Assets/', 'wgpu-host/',
  'Directory.Build.props', 'Directory.Build.targets', 'global.json', 'legal.html', 'docs/legal/',
  '.config/dotnet-tools.json',
  'ffmpeg', 'ffmpeg.exe', 'yt-dlp', 'yt-dlp.exe',
];

export function touchesApp(files = []) {
  return files.some((f) => APP_PATHS.some((p) => (p.endsWith('/') ? f.startsWith(p) : f === p)));
}

/**
 * Decides what a merge into main does.
 * @returns {{release:boolean, reason:string, version?:string, previous?:string}}
 */
export function planStable({ latest, labels = [], title = '', files = [], force = false }) {
  const names = labels.map((l) => String(l).toLowerCase());
  if (!force && names.includes('skip-release')) return { release: false, reason: 'the pull request is labelled skip-release' };
  if (!force && !touchesApp(files)) return { release: false, reason: 'nothing that ships in the app changed' };
  if (!latest) return { release: false, reason: 'could not work out the current version (no tags and no feed)' };

  const asked = explicitVersion(title);
  if (asked) {
    if (compareVersions(asked, latest) <= 0) {
      return { release: false, reason: `the title asks for ${asked}, which is not newer than ${latest}` };
    }
    return { release: true, reason: `version ${asked} taken from the title`, version: asked, previous: latest };
  }
  const kind = bumpKind(labels);
  return { release: true, reason: `${kind} bump from ${latest}`, version: bump(latest, kind), previous: latest };
}

/** Betas are previews of the next patch: 7.1.0 stable -> 7.1.1-beta.<n>. They sort below 7.1.1 itself. */
export function betaVersion(latest, build) {
  const n = Number.parseInt(build, 10);
  if (!Number.isInteger(n) || n < 1) throw new Error(`"${build}" is not a build number.`);
  return `${bump(latest, 'patch')}-beta.${n}`;
}

// ── pull request description -> changelog entry ─────────────────────────────────────────────

// The website only knows these icons (CHANGELOG_ICONS in frontend/src/data/site.jsx).
export const ICONS = [
  'Waves', 'Radio', 'Zap', 'Shield', 'FileCode2', 'Package', 'MonitorPlay', 'BarChart3', 'Activity', 'Layers',
  'Sparkles', 'Music', 'Mic2', 'Terminal', 'RefreshCw', 'ListMusic', 'Globe', 'Crosshair', 'Dices', 'Trophy',
  'Minimize2', 'Palette', 'Users', 'NotebookPen', 'Bug', 'GitFork',
];

const ICON_HINTS = [
  [/fix|bug|crash|broken|regress/i, 'Bug'],
  [/spotify|music|audio|playback|mix|dj|sound/i, 'Music'],
  [/lyric|karaoke|vocal/i, 'Mic2'],
  [/visuali[sz]er|spectrum|wave/i, 'Waves'],
  [/shared play|room|listener|communit|friend/i, 'Users'],
  [/obs|overlay|stream|fullscreen|window/i, 'MonitorPlay'],
  [/update|install|release|beta/i, 'RefreshCw'],
  [/capsule|package|world/i, 'Package'],
  [/security|trust|privacy|sign/i, 'Shield'],
  [/playlist|queue|library/i, 'ListMusic'],
  [/theme|color|design|ui|look/i, 'Palette'],
  [/perf|fast|speed|quick/i, 'Zap'],
  [/discord|presence/i, 'Radio'],
  [/web|site|browser|link/i, 'Globe'],
  [/dev|script|api|cli/i, 'Terminal'],
];

function iconFor(title) {
  for (const [pattern, icon] of ICON_HINTS) if (pattern.test(title)) return icon;
  return 'Sparkles';
}

const clip = (text, max) => {
  const s = String(text ?? '').replace(/\s+/g, ' ').trim();
  return s.length <= max ? s : `${s.slice(0, max - 1).trimEnd()}…`;
};

// Markdown the website would show as literal punctuation.
const plain = (text) =>
  String(text ?? '')
    .replace(/!\[([^\]]*)\]\([^)]*\)/g, '$1')
    .replace(/\[([^\]]+)\]\([^)]*\)/g, '$1')
    .replace(/(\*\*|__)(.+?)\1/g, '$2')
    .replace(/(^|[\s(])[*_]([^*_\s][^*_]*?)[*_](?=$|[\s).,!?:;])/g, '$1$2')
    .replace(/`([^`]+)`/g, '$1')
    .replace(/[<>]/g, '');

/**
 * Turns a pull request description into the entry shape `/spectralis/v1/changelog` serves.
 *
 *   A sentence or two up top becomes the summary.
 *
 *   ## Playback {Music}          <- a group; the icon in braces is optional
 *   - a bullet
 *
 *   Metrics: Gapless, 2x faster  <- optional chips
 *
 * Bullets before any heading land in a "What changed" group. An empty description still makes a
 * valid entry, so a release is never blocked on prose.
 */
export function parseChangelog(body, { version, title = '' }) {
  const text = String(body ?? '').replace(/<!--[\s\S]*?-->/g, '').replace(/\r\n?/g, '\n');
  const summary = [];
  const groups = [];
  let metrics = [];
  let label = '';
  let current = null;
  let inFence = false;

  for (const raw of text.split('\n')) {
    const line = raw.trim();
    if (line.startsWith('```')) { inFence = !inFence; continue; }
    if (inFence || line === '' || /^-{3,}$/.test(line)) continue;

    const metricLine = /^metrics\s*:\s*(.+)$/i.exec(line);
    if (metricLine) {
      metrics = metricLine[1].split(/[,|·]/).map((m) => clip(plain(m), 60)).filter(Boolean).slice(0, 8);
      continue;
    }

    const heading = /^(#{1,6})\s+(.*)$/.exec(line);
    if (heading) {
      let name = heading[2].trim();
      if (heading[1].length === 1 && !label && groups.length === 0) { label = clip(plain(name), 120); continue; }
      let icon = null;
      const tagged = /\{\s*([A-Za-z0-9]+)\s*\}\s*$/.exec(name);
      if (tagged) {
        name = name.slice(0, tagged.index).trim();
        icon = ICONS.find((i) => i.toLowerCase() === tagged[1].toLowerCase()) ?? null;
      }
      name = clip(plain(name), 120);
      if (!name) continue;
      current = { icon: icon ?? iconFor(name), title: name, bullets: [] };
      groups.push(current);
      continue;
    }

    const bullet = /^(?:[-*+]|\d+[.)])\s+(?:\[[ xX]\]\s+)?(.*)$/.exec(line);
    if (bullet) {
      const item = clip(plain(bullet[1]), 800);
      if (!item) continue;
      if (!current) { current = { icon: 'Sparkles', title: 'What changed', bullets: [] }; groups.push(current); }
      if (current.bullets.length < 20 && !current.bullets.includes(item)) current.bullets.push(item);
      continue;
    }

    if (!current) summary.push(plain(line));
    else if (current.bullets.length > 0) {
      // a wrapped bullet: fold the continuation line back into it
      const last = current.bullets.length - 1;
      current.bullets[last] = clip(`${current.bullets[last]} ${plain(line)}`, 800);
    }
  }

  const cleanTitle = clip(plain(String(title).replace(/^\s*(?:release\s+)?v?\d+\.\d+\.\d+\s*[:,—–-]?\s*/i, '')), 120);
  return {
    version,
    label: label || cleanTitle || `Spectralis ${version}`,
    date: 'Latest',
    summary: clip(summary.join(' '), 4000),
    metrics,
    groups: groups.filter((g) => g.bullets.length > 0).slice(0, 12),
  };
}

/**
 * Puts a new entry at the top of the published changelog. Whatever was "Latest" becomes dated with
 * the day it stopped being latest only if it had no real date; re-running for the same version
 * replaces that entry instead of duplicating it. The backend holds at most 100 entries.
 */
export function mergeChangelog(existing, entry) {
  const rest = (Array.isArray(existing) ? existing : [])
    .filter((e) => e && e.version !== entry.version)
    .map((e) => (e.date === 'Latest' ? { ...e, date: 'Previous' } : e));
  return [entry, ...rest].slice(0, 100);
}

// ── release index ───────────────────────────────────────────────────────────────────────────

export const REPO_URL = 'https://github.com/Dareto-Dream/Spectralis';
export const MAX_INDEX_ENTRIES = 200;
export const KEEP_BETAS = 8;

/** One row of `/spectralis/v1/releases`: where a version's installers and update feed live. */
export function releaseEntry({ version, date, files = [], repoUrl = REPO_URL }) {
  const v = parseVersion(version);
  if (!v) throw new Error(`"${version}" is not a version.`);
  const tag = `v${version}`;
  const base = `${repoUrl}/releases/download/${tag}`;
  const has = (name) => files.includes(name);
  const platforms = {};
  if (has('releases.win-x64.json')) {
    platforms['win-x64'] = {
      installer: has('Spectralis-win-x64-Setup.exe') ? `${base}/Spectralis-win-x64-Setup.exe` : '',
      portable: has('Spectralis-win-x64-Portable.zip') ? `${base}/Spectralis-win-x64-Portable.zip` : '',
    };
  }
  if (has('releases.linux-x64.json')) {
    platforms['linux-x64'] = {
      installer: has('Spectralis-linux-x64.AppImage') ? `${base}/Spectralis-linux-x64.AppImage` : '',
      portable: '',
    };
  }
  return {
    version,
    channel: v.pre ? 'beta' : 'stable',
    date,
    tag,
    feedBase: base,
    notesUrl: `${repoUrl}/releases/tag/${tag}`,
    platforms,
  };
}

/**
 * Adds a release to the index, newest first. Every stable release stays (that is the "older
 * versions" list); only the newest few betas do, and a beta whose stable has shipped is dropped.
 * @returns {{index:object[], droppedBetas:string[]}} droppedBetas are tags safe to delete from GitHub
 */
export function mergeReleases(existing, entry, { keepBetas = KEEP_BETAS } = {}) {
  const all = [entry, ...(Array.isArray(existing) ? existing : []).filter((e) => e && e.version !== entry.version)]
    .sort((a, b) => compareVersions(b.version, a.version));
  const newestStable = all.find((e) => e.channel === 'stable')?.version ?? null;

  const index = [];
  const droppedBetas = [];
  let betas = 0;
  for (const e of all) {
    if (e.channel !== 'beta') { index.push(e); continue; }
    const superseded = newestStable !== null && compareVersions(e.version, newestStable) < 0;
    if (!superseded && betas < keepBetas) { betas++; index.push(e); } else droppedBetas.push(e.tag);
  }
  return { index: index.slice(0, MAX_INDEX_ENTRIES), droppedBetas };
}
