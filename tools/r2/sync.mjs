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

import { createReadStream } from 'node:fs';
import { readdir, readFile, stat } from 'node:fs/promises';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import {
  AbortMultipartUploadCommand, DeleteObjectsCommand, HeadObjectCommand, ListMultipartUploadsCommand,
  ListObjectsV2Command, S3Client,
} from '@aws-sdk/client-s3';
import { Upload } from '@aws-sdk/lib-storage';
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

function s3Storage(client, bucket) {
  return {
    async list() {
      const objects = [];
      let token;
      do {
        const page = await client.send(new ListObjectsV2Command({ Bucket: bucket, ContinuationToken: token }));
        for (const o of page.Contents ?? []) objects.push({ key: o.Key, size: o.Size ?? 0 });
        token = page.IsTruncated ? page.NextContinuationToken : undefined;
      } while (token);
      return objects;
    },

    async abortStaleUploads() {
      const page = await client.send(new ListMultipartUploadsCommand({ Bucket: bucket }));
      for (const u of page.Uploads ?? []) {
        await client.send(new AbortMultipartUploadCommand({ Bucket: bucket, Key: u.Key, UploadId: u.UploadId }));
      }
      return (page.Uploads ?? []).length;
    },

    async remove(keys) {
      for (let i = 0; i < keys.length; i += 1000) {
        const batch = keys.slice(i, i + 1000).map((Key) => ({ Key }));
        const result = await client.send(new DeleteObjectsCommand({ Bucket: bucket, Delete: { Objects: batch, Quiet: true } }));
        if (result.Errors?.length) throw new Error(`Could not delete ${result.Errors.map((e) => e.Key).join(', ')}`);
      }
    },

    async put(key, body, size) {
      const upload = new Upload({
        client,
        params: { Bucket: bucket, Key: key, Body: body.bytes ?? createReadStream(body.path), ContentLength: size,
          ContentType: key.endsWith('.json') ? 'application/json' : 'application/octet-stream' },
        partSize: 16 * 1024 * 1024,
        queueSize: 2,
        leavePartsOnError: false, // a failed upload must not leave billable parts behind
      });
      await upload.done();
    },

    async sizeOf(key) {
      try {
        const head = await client.send(new HeadObjectCommand({ Bucket: bucket, Key: key }));
        return head.ContentLength ?? null;
      } catch (error) {
        if (error?.$metadata?.httpStatusCode === 404) return null;
        throw error;
      }
    },
  };
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
  const client = new S3Client({
    region: 'auto',
    endpoint: `https://${accountId}.r2.cloudflarestorage.com`,
    credentials: { accessKeyId: requireEnv('R2_ACCESS_KEY_ID'), secretAccessKey: requireEnv('R2_SECRET_ACCESS_KEY') },
  });

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
