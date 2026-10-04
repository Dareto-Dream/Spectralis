import assert from 'node:assert/strict';
import { test } from 'node:test';
import { WebhookApi, WebhookApiError } from './webhookApi.ts';

interface Captured {
  url: string;
  init: RequestInit;
}

function stubFetch(respond: () => Response): { fetchImpl: typeof fetch; captured: Captured[] } {
  const captured: Captured[] = [];
  const fetchImpl = (async (input: string | URL | Request, init?: RequestInit) => {
    captured.push({ url: String(input), init: init ?? {} });
    return respond();
  }) as typeof fetch;
  return { fetchImpl, captured };
}

test('submit posts to the room webhook with the key header', async () => {
  const { fetchImpl, captured } = stubFetch(
    () => new Response(JSON.stringify({ submissionId: 's', status: 'queued', position: 2 })),
  );
  const api = new WebhookApi('https://api.example.test/', 'room 1', 'wk_secret', fetchImpl);

  const result = await api.submit({ source: 'twitch', userId: '1', displayName: 'A', url: 'https://x.test/a' });

  assert.deepEqual(result, { submissionId: 's', status: 'queued', position: 2 });
  assert.equal(captured[0].url, 'https://api.example.test/streamer-queue/v2/rooms/room%201/webhook/submit');
  const headers = captured[0].init.headers as Record<string, string>;
  assert.equal(headers['x-spectralis-webhook-key'], 'wk_secret');
  assert.deepEqual(JSON.parse(String(captured[0].init.body)), {
    source: 'twitch',
    userId: '1',
    displayName: 'A',
    url: 'https://x.test/a',
  });
});

test('status posts the person and returns the queue snapshot', async () => {
  const { fetchImpl, captured } = stubFetch(
    () =>
      new Response(
        JSON.stringify({ acceptingSubmissions: true, queueLength: 1, nowPlaying: null, yourPositions: [1] }),
      ),
  );

  const status = await new WebhookApi('https://a.test', 'r', 'k', fetchImpl).status({ source: 'youtube', userId: 'UC1' });

  assert.equal(status.queueLength, 1);
  assert.ok(captured[0].url.endsWith('/rooms/r/webhook/status'));
});

test("errors carry the server's message and status", async () => {
  const { fetchImpl } = stubFetch(
    () => new Response(JSON.stringify({ error: 'The queue is full right now.', status: 400 }), { status: 400 }),
  );

  await assert.rejects(
    new WebhookApi('https://a.test', 'r', 'k', fetchImpl).submit({ source: 's', userId: '1', displayName: 'A', url: 'https://x.test' }),
    (err: unknown) => err instanceof WebhookApiError && err.status === 400 && err.message === 'The queue is full right now.',
  );
});

test('non-json error bodies still produce a message', async () => {
  const { fetchImpl } = stubFetch(() => new Response('upstream exploded', { status: 502 }));

  await assert.rejects(
    new WebhookApi('https://a.test', 'r', 'k', fetchImpl).status({ source: 's', userId: '1' }),
    (err: unknown) => err instanceof WebhookApiError && err.status === 502 && err.message === 'upstream exploded',
  );
});
