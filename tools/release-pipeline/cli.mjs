#!/usr/bin/env node
// The I/O half of the release pipeline; the rules are in lib.mjs. Run from the repo root by the
// workflows in .github/workflows. Each command writes its results to $GITHUB_OUTPUT when that is
// set and prints them either way, so it can be tried locally:
//
//   node tools/release-pipeline/cli.mjs plan-stable      what would merging HEAD into main release?
//   node tools/release-pipeline/cli.mjs plan-beta        the next beta version
//   node tools/release-pipeline/cli.mjs publish-content  add the release to the changelog and release index
//
// plan-* need `gh` signed in (GH_TOKEN in CI). publish-content needs SPECTRALIS_ADMIN_TOKEN.

import { execFileSync } from 'node:child_process';
import { appendFileSync, readFileSync, readdirSync, writeFileSync } from 'node:fs';
import process from 'node:process';
import {
  betaVersion, latestStable, mergeChangelog, mergeReleases, parseChangelog, planStable, releaseEntry, versionsInFeed,
} from './lib.mjs';

const API_BASE = (process.env.SPECTRALIS_API_BASE || 'https://spectralis-api.deltavdevs.com').replace(/\/+$/, '');
const CDN_BASE = (process.env.SPECTRALIS_CDN_BASE || 'https://spectralis-cdn.deltavdevs.com').replace(/\/+$/, '');
const REPO = process.env.GITHUB_REPOSITORY || 'Dareto-Dream/Spectralis';

function run(cmd, args) {
  return execFileSync(cmd, args, { encoding: 'utf8', maxBuffer: 64 * 1024 * 1024 }).trim();
}

function tryRun(cmd, args) {
  try { return run(cmd, args); } catch { return ''; }
}

function output(values) {
  for (const [key, value] of Object.entries(values)) {
    console.log(`${key}=${value}`);
    if (process.env.GITHUB_OUTPUT) appendFileSync(process.env.GITHUB_OUTPUT, `${key}=${value}\n`);
  }
}

function flags(argv) {
  const out = {};
  for (let i = 0; i < argv.length; i++) {
    if (argv[i].startsWith('--')) out[argv[i].slice(2)] = argv[i + 1]?.startsWith('--') || argv[i + 1] === undefined ? 'true' : argv[++i];
  }
  return out;
}

/** Newest stable version: git tags and both platform feeds on the CDN, whichever is highest. */
async function currentStable() {
  const candidates = tryRun('git', ['tag', '--list']).split('\n');
  for (const rid of ['win-x64', 'linux-x64']) {
    try {
      const res = await fetch(`${CDN_BASE}/releases.${rid}.json`, { signal: AbortSignal.timeout(20_000) });
      if (res.ok) candidates.push(...versionsInFeed(await res.text()));
    } catch { /* the tags may be enough */ }
  }
  return latestStable(candidates);
}

async function planStableCommand(args) {
  if (args.version && !/^\d+\.\d+\.\d+$/.test(args.version)) {
    throw new Error('--version must be a stable version such as 7.1.1.');
  }
  const sha = args.sha || process.env.GITHUB_SHA || run('git', ['rev-parse', 'HEAD']);
  const latest = await currentStable();
  const force = args.force === 'true';

  let pr = null;
  const pulls = JSON.parse(tryRun('gh', ['api', `repos/${REPO}/commits/${sha}/pulls`]) || '[]');
  pr = pulls.find((p) => p.merged_at && p.base?.ref === 'main') ?? pulls.find((p) => p.merged_at) ?? null;

  let files = [];
  if (pr) {
    files = run('gh', ['api', '--paginate', `repos/${REPO}/pulls/${pr.number}/files`, '--jq', '.[].filename']).split('\n').filter(Boolean);
  } else {
    // a direct push: judge it by the commit itself
    files = tryRun('git', ['diff-tree', '--no-commit-id', '--name-only', '-r', '-m', sha]).split('\n').filter(Boolean);
  }

  const title = args.version ? `v${args.version}` : pr?.title ?? tryRun('git', ['log', '-1', '--format=%s', sha]);
  const plan = planStable({
    latest,
    labels: (pr?.labels ?? []).map((l) => l.name),
    title,
    files,
    force: force || Boolean(args.version),
  });

  const notes = args['notes-file'] || 'release-notes.md';
  writeFileSync(notes, `${pr?.body ?? ''}\n`);
  console.log(plan.release ? `Releasing ${plan.version} (${plan.reason}).` : `No app release: ${plan.reason}.`);
  output({
    release: plan.release ? 'true' : 'false',
    version: plan.version ?? '',
    previous: plan.previous ?? latest ?? '',
    pr: pr?.number ?? '',
    title: JSON.stringify(pr?.title ?? title ?? ''),
    notes_file: notes,
  });
}

async function planBetaCommand(args) {
  const latest = await currentStable();
  if (!latest) throw new Error('Could not work out the current stable version (no tags and no feed).');
  const version = betaVersion(latest, args.build || process.env.GITHUB_RUN_NUMBER || '1');

  // What the beta has that stable does not: commit subjects since main.
  const subjects = tryRun('git', ['log', '--no-merges', '--format=- %s', 'origin/main..HEAD']).split('\n').filter(Boolean).slice(0, 60);
  const notes = args['notes-file'] || 'release-notes.md';
  writeFileSync(notes, [
    `Beta build of the next Spectralis release (after ${latest}). Opt in under Settings, Updates, Update channel.`,
    '',
    subjects.length ? '## In this beta' : '',
    ...subjects,
    '',
  ].join('\n'));
  console.log(`Beta ${version}.`);
  output({ version, previous: latest, notes_file: notes });
}

async function api(method, path, body) {
  const token = process.env.SPECTRALIS_ADMIN_TOKEN;
  const res = await fetch(`${API_BASE}${path}`, {
    method,
    headers: {
      ...(body ? { 'content-type': 'application/json' } : {}),
      ...(method !== 'GET' && token ? { authorization: `Bearer ${token}` } : {}),
    },
    body: body ? JSON.stringify(body) : undefined,
    signal: AbortSignal.timeout(30_000),
  });
  if (!res.ok) throw new Error(`${method} ${path} -> ${res.status} ${(await res.text()).slice(0, 300)}`);
  return res.json();
}

async function publishContentCommand(args) {
  const version = args.version;
  if (!version) throw new Error('--version is required.');
  if (!process.env.SPECTRALIS_ADMIN_TOKEN) throw new Error('SPECTRALIS_ADMIN_TOKEN is not set.');
  const files = args['files-dir'] ? readdirSync(args['files-dir']) : [];
  const date = args.date || new Date().toISOString().slice(0, 10);
  const entry = releaseEntry({ version, date, files, repoUrl: `https://github.com/${REPO}` });

  // The index first: it is what makes the version downloadable. A backend that predates the
  // endpoint answers 404; say so plainly instead of failing the release that already shipped.
  let existing;
  try {
    existing = await api('GET', '/spectralis/v1/releases');
  } catch (error) {
    throw new Error(`The backend has no release index yet (${error.message}). Deploy the backend, then re-run this job.`);
  }
  const { index, droppedBetas } = mergeReleases(existing, entry);
  await api('PUT', '/spectralis/v1/admin/releases', index);
  console.log(`Release index: ${index.length} versions, ${version} listed as ${entry.channel}.`);

  if (entry.channel === 'stable') {
    const body = args['notes-file'] ? readFileSync(args['notes-file'], 'utf8') : '';
    let title = args.title ?? '';
    try { title = JSON.parse(title); } catch { /* a plain string */ }
    const changelog = mergeChangelog(await api('GET', '/spectralis/v1/changelog'), parseChangelog(body, { version, title }));
    await api('PUT', '/spectralis/v1/admin/changelog', changelog);
    console.log(`Changelog: ${version} is now the latest of ${changelog.length} entries.`);
  }

  output({ dropped_betas: droppedBetas.join(' ') });
}

const [command, ...rest] = process.argv.slice(2);
const commands = { 'plan-stable': planStableCommand, 'plan-beta': planBetaCommand, 'publish-content': publishContentCommand };
if (!commands[command]) {
  console.error(`usage: node tools/release-pipeline/cli.mjs <${Object.keys(commands).join('|')}> [--flags]`);
  process.exit(2);
}
commands[command](flags(rest)).catch((error) => {
  console.error(`${command} failed: ${error.message}`);
  process.exit(1);
});
