import assert from 'node:assert/strict';
import { test } from 'node:test';
import { mapLiveChatItems, YouTubeTransport } from './youtube.ts';
import type { LiveChatItem } from './youtube.ts';

const text = (message: string, at: string, channelId = 'UC1', name = 'Fan'): LiveChatItem => ({
  snippet: { type: 'textMessageEvent', displayMessage: message, publishedAt: at },
  authorDetails: { channelId, displayName: name },
});

const T0 = Date.parse('2026-10-03T12:00:00Z');

test('keeps new viewer text messages keyed on the channel id', () => {
  const [message] = mapLiveChatItems([text('!sr https://youtu.be/a', '2026-10-03T12:00:05Z', 'UC9', 'Ann')], T0);

  assert.deepEqual(message, { source: 'youtube', userId: 'UC9', displayName: 'Ann', text: '!sr https://youtu.be/a' });
});

test('drops history from before the bridge started', () => {
  assert.equal(mapLiveChatItems([text('!sr https://youtu.be/old', '2026-10-03T11:59:00Z')], T0).length, 0);
});

test('ignores super chats, membership events and malformed items', () => {
  const items: LiveChatItem[] = [
    { snippet: { type: 'superChatEvent', displayMessage: '$5', publishedAt: '2026-10-03T12:01:00Z' }, authorDetails: { channelId: 'UC1' } },
    { snippet: { type: 'textMessageEvent', displayMessage: '', publishedAt: '2026-10-03T12:01:00Z' }, authorDetails: { channelId: 'UC1' } },
    { snippet: { type: 'textMessageEvent', displayMessage: 'hi', publishedAt: '2026-10-03T12:01:00Z' }, authorDetails: {} },
    {},
  ];
  assert.equal(mapLiveChatItems(items, T0).length, 0);
});

function jsonResponse(body: unknown, status = 200): Response {
  return new Response(JSON.stringify(body), { status, headers: { 'content-type': 'application/json' } });
}

test('resolves the live chat id from a video id, then polls it and delivers messages once', async () => {
  const urls: string[] = [];
  const fetchImpl = (async (input: string | URL | Request) => {
    const url = String(input);
    urls.push(url);
    if (url.includes('/videos?')) {
      return jsonResponse({ items: [{ liveStreamingDetails: { activeLiveChatId: 'CHAT1' } }] });
    }
    return jsonResponse({
      items: [text('!queue', '2026-10-03T12:00:10Z')],
      nextPageToken: 'next',
      pollingIntervalMillis: 60_000,
    });
  }) as typeof fetch;

  const transport = new YouTubeTransport({ apiKey: 'k', videoId: 'VID', fetchImpl, now: () => T0 });
  const received: string[] = [];
  await new Promise<void>((resolve) => {
    transport.start((message) => {
      received.push(message.text);
      resolve();
    });
  });
  transport.stop();

  assert.deepEqual(received, ['!queue']);
  assert.ok(urls[0].includes('videos?') && urls[0].includes('id=VID'));
  assert.ok(urls[1].includes('liveChatId=CHAT1') && urls[1].includes('key=k'));
});

test('a chat that has ended stops the poller instead of hammering the api', async () => {
  let calls = 0;
  const fetchImpl = (async () => {
    calls++;
    return new Response('{"error":{"errors":[{"reason":"liveChatEnded"}]}}', { status: 403 });
  }) as typeof fetch;

  const transport = new YouTubeTransport({ apiKey: 'k', liveChatId: 'CHAT1', fetchImpl, now: () => T0 });
  transport.start(() => {});
  await new Promise((resolve) => setTimeout(resolve, 50));

  assert.equal(calls, 1);
  transport.stop();
});
