import assert from 'node:assert/strict';
import { test } from 'node:test';
import type { Client } from 'discord.js';
process.env.DISCORD_TOKEN = 'test-only';
process.env.DISCORD_CLIENT_ID = 'test-only';
process.env.SQ_API_BASE_URL = 'https://staging.example';
process.env.DB_PATH = ':memory:';
const { startPolling, stopPolling } = await import('./nowPlayingPoller.js');
const sleep = (ms: number) => new Promise(resolve => setTimeout(resolve, ms));
async function until(predicate: () => boolean) {
  for (let i = 0; i < 100 && !predicate(); i++) await sleep(10);
  assert.ok(predicate(), 'poller did not reach the expected state');
}

test('socket refreshes cannot duplicate a pending post and queue-only changes update its embed', async () => {
  const originalFetch = globalThis.fetch;
  const originalSocket = globalThis.WebSocket;
  const sockets: FakeSocket[] = [];
  class FakeSocket {
    listeners = new Map<string, (event: { data: string }) => void>();
    constructor() { sockets.push(this); }
    addEventListener(type: string, listener: (event: { data: string }) => void) { this.listeners.set(type, listener); }
    send() {} close() {}
    changed() { this.listeners.get('message')?.({ data: JSON.stringify({ v: 2, t: 'sq.changed' }) }); }
  }
  globalThis.WebSocket = FakeSocket as unknown as typeof WebSocket;
  let fetches = 0, sends = 0, edits = 0, queueLength = 2;
  let release!: () => void;
  const pending = new Promise<void>(resolve => { release = resolve; });
  globalThis.fetch = async () => { fetches++; return Response.json({ enabled: true, acceptingSubmissions: true,
    nowPlayingId: 'track', nowPlayingTitle: 'Song', queueLength, submissions: [] }); };
  const message = { id: 'status', edit: async () => { edits++; } };
  const channel = { isSendable: () => true, messages: { fetch: async () => message },
    send: async () => { sends++; await pending; return message; } };
  const client = { channels: { fetch: async () => channel } } as unknown as Client;
  const link = { guildId: 'guild', channelId: 'channel', roomId: 'room', botToken: 'test', linkedBy: 'owner', linkedAt: '', openMessage: null, closedMessage: null };
  try {
    startPolling(client, link);
    await until(() => sends === 1);
    sockets[0].changed();
    await sleep(200);
    assert.equal(fetches, 1);
    assert.equal(sends, 1);
    release();
    await sleep(20);
    queueLength = 3;
    sockets[0].changed();
    await until(() => edits === 1);
    assert.equal(sends, 1);
  } finally { release(); stopPolling(link); globalThis.fetch = originalFetch; globalThis.WebSocket = originalSocket; }
});
