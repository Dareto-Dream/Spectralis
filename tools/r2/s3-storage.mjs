// The R2 bucket seen through the small Storage interface that sync-core.mjs (and the visualizer upload) work against.

import { createReadStream } from 'node:fs';
import {
  AbortMultipartUploadCommand, DeleteObjectsCommand, HeadObjectCommand, ListMultipartUploadsCommand,
  ListObjectsV2Command, S3Client,
} from '@aws-sdk/client-s3';
import { Upload } from '@aws-sdk/lib-storage';

export function r2Client(accountId, accessKeyId, secretAccessKey) {
  return new S3Client({
    region: 'auto',
    endpoint: `https://${accountId}.r2.cloudflarestorage.com`,
    credentials: { accessKeyId, secretAccessKey },
  });
}

const DEFAULT_TYPE = key => (key.endsWith('.json') ? 'application/json' : 'application/octet-stream');

export function s3Storage(client, bucket) {
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

    async put(key, body, size, contentType = DEFAULT_TYPE(key)) {
      const upload = new Upload({
        client,
        params: { Bucket: bucket, Key: key, Body: body.bytes ?? createReadStream(body.path), ContentLength: size,
          ContentType: contentType },
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
