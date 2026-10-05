#!/usr/bin/env node
// Loads the content that used to live as files on the legacy CDN into the Spectralis backend:
// warning.json, changelog.json, community.json (with its avatars), and the creator keys under keys/
// (with their avatars). Safe to run again: every step replaces what the backend already has.
//
//   SPECTRALIS_ADMIN_TOKEN=... node import.mjs --source Y:/spectralis --api https://spectralis-api.deltavdevs.com
//   node import.mjs --source Y:/spectralis --api http://127.0.0.1:8094 --apply
//
// Without --apply it only reads and validates what it would send.

import { existsSync } from 'node:fs';
import { readdir, readFile } from 'node:fs/promises';
import path from 'node:path';

const args = { source: 'Y:/spectralis', api: 'https://spectralis-api.deltavdevs.com', apply: false };
const argv = process.argv.slice(2);
for (let i = 0; i < argv.length; i++) {
  if (argv[i] === '--source') args.source = argv[++i];
  else if (argv[i] === '--api') args.api = argv[++i].replace(/\/+$/, '');
  else if (argv[i] === '--apply') args.apply = true;
  else throw new Error(`Unknown argument: ${argv[i]}`);
}

const token = process.env.SPECTRALIS_ADMIN_TOKEN?.trim();
if (args.apply && !token) throw new Error('Set SPECTRALIS_ADMIN_TOKEN to --apply.');

const readJson = async (file) => JSON.parse((await readFile(file, 'utf8')).replace(/^\uFEFF/, ''));
const slugOf = (url) => path.basename(new URL(url).pathname).replace(/\.[a-z0-9]+$/i, '').toLowerCase();

let failures = 0;
const say = (line) => console.log(line);

async function put(route, body, { raw = false } = {}) {
  if (!args.apply) return { ok: true, dry: true };
  const res = await fetch(`${args.api}/spectralis/v1/admin/${route}`, {
    method: 'PUT',
    headers: { authorization: `Bearer ${token}`, ...(raw ? {} : { 'content-type': 'application/json' }) },
    body: raw ? body : JSON.stringify(body),
  });
  const text = await res.text();
  let json = null;
  try { json = JSON.parse(text); } catch { /* not json */ }
  if (!res.ok) throw new Error(`${route}: HTTP ${res.status} ${json?.error ?? json?.message ?? text.slice(0, 200)}`);
  return json ?? {};
}

async function step(label, fn) {
  try {
    say(`${label} ... ${await fn()}`);
  } catch (error) {
    failures++;
    say(`${label} ... FAILED: ${error.message}`);
  }
}

const file = (name) => path.join(args.source, name);

await step('warnings', async () => {
  const list = await readJson(file('warning.json'));
  await put('warnings', list);
  return `${list.length} entries`;
});

await step('changelog', async () => {
  const list = await readJson(file('changelog.json'));
  await put('changelog', list);
  return `${list.length} releases`;
});

await step('community', async () => {
  const list = await readJson(file('community.json'));
  let avatars = 0;
  for (const person of list) {
    if (!person.avatar) continue;
    const slug = slugOf(person.avatar);
    const local = path.join(args.source, 'avatars', path.basename(new URL(person.avatar).pathname));
    if (!existsSync(local)) throw new Error(`avatar file missing for ${person.name}: ${local}`);
    const result = await put(`community/avatars/${slug}`, await readFile(local), { raw: true });
    person.avatar = result.dry ? `https://example.invalid/${slug}` : result.url;
    avatars++;
  }
  await put('community', list);
  return `${list.length} people, ${avatars} avatars`;
});

await step('creators', async () => {
  const dir = file('keys');
  const names = (await readdir(dir)).filter((n) => /^[0-9a-f]{64}\.json$/.test(n));
  let avatars = 0;
  for (const name of names) {
    const fingerprint = name.replace(/\.json$/, '');
    const key = await readJson(path.join(dir, name));
    if (key.fingerprint && key.fingerprint !== fingerprint) throw new Error(`${name} names a different fingerprint`);
    await put(`creators/${fingerprint}`, {
      keyId: key.keyId,
      displayName: key.displayName,
      profileUrl: key.profileUrl ?? undefined,
      status: key.status,
      allowedCapabilities: key.allowedCapabilities,
      createdAtUtc: key.createdAtUtc,
      revokedAtUtc: key.revokedAtUtc,
    });
    const avatar = path.join(dir, 'avatars', `${fingerprint}.png`);
    if (existsSync(avatar)) {
      await put(`creators/${fingerprint}/avatar`, await readFile(avatar), { raw: true });
      avatars++;
    }
  }
  return `${names.length} keys, ${avatars} avatars`;
});

say(args.apply ? (failures ? `Finished with ${failures} failed step(s).` : 'Imported.') : 'Dry run: validated the files, sent nothing. Re-run with --apply.');
process.exit(failures ? 1 : 0);
