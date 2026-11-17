import assert from 'node:assert/strict';
import { test } from 'node:test';
process.env.DISCORD_TOKEN = 'test-only';
process.env.DISCORD_CLIENT_ID = 'test-only';
process.env.SQ_API_BASE_URL = 'https://staging.example';
const api = await import('./sqApi.js');

test('link and file submissions carry their Discord channel for priority routing', async () => {
  const original = globalThis.fetch;
  const bodies: unknown[] = [];
  globalThis.fetch = async (_url, init) => { bodies.push(init?.body); return Response.json({ submissionId: 'request', status: 'pending' }); };
  try {
    await api.submitRequest('room', 'user', 'Name', 'https://suno.com/song/test', 'normal', {}, 'vip-channel');
    assert.equal(JSON.parse(bodies[0] as string).discordChannelId, 'vip-channel');
    await api.uploadFile('room', 'user', 'Name', new Uint8Array([1]), 'audio.wav', 'normal', null, 'vip-channel');
    assert.equal((bodies[1] as FormData).get('discordChannelId'), 'vip-channel');
  } finally { globalThis.fetch = original; }
});

test('opening a disabled room reports that the host must start the session', async () => {
  const original = globalThis.fetch;
  globalThis.fetch = async () => Response.json({ enabled: false, acceptingSubmissions: true });
  try { await assert.rejects(api.setAcceptingSubmissions('room', 'scoped-token', true), /Start the session/); }
  finally { globalThis.fetch = original; }
});

test('an expired bot token cannot masquerade as a successful elevated response', async () => {
  const original = globalThis.fetch;
  globalThis.fetch = async () => Response.json({ enabled: true, queueLength: 2 });
  try { await assert.rejects(api.getRoomAsBot('room', 'expired'), /pairing expired/); }
  finally { globalThis.fetch = original; }
});
