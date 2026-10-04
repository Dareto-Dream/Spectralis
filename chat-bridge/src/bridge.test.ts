import assert from 'node:assert/strict';
import { test } from 'node:test';
import { Bridge } from './bridge.ts';
import type { QueueApi } from './bridge.ts';
import type { ChatMessage } from './chat.ts';
import { WebhookApiError } from './webhookApi.ts';

function fakeApi(overrides: Partial<QueueApi> = {}) {
  const calls: { submit: unknown[]; status: unknown[] } = { submit: [], status: [] };
  const api: QueueApi = {
    async submit(input) {
      calls.submit.push(input);
      return { submissionId: 's1', status: 'queued', position: 3 };
    },
    async status(person) {
      calls.status.push(person);
      return { acceptingSubmissions: true, queueLength: 7, nowPlaying: null, yourPositions: [] };
    },
    ...overrides,
  };
  return { api, calls };
}

const say = (text: string, userId = 'u1'): ChatMessage => ({
  source: 'twitch',
  userId,
  displayName: 'Viewer',
  text,
});

test('a request is submitted as that person and confirmed with its position', async () => {
  const { api, calls } = fakeApi();
  const reply = await new Bridge(api).handle(say('!sr https://youtu.be/a'));

  assert.equal(reply, '@Viewer added to the queue at #3.');
  assert.deepEqual(calls.submit, [
    { source: 'twitch', userId: 'u1', displayName: 'Viewer', url: 'https://youtu.be/a' },
  ]);
});

test('a room that needs approval says so', async () => {
  const { api } = fakeApi({ submit: async () => ({ submissionId: 's', status: 'pending_approval' }) });
  assert.equal(
    await new Bridge(api).handle(say('!sr https://youtu.be/a')),
    '@Viewer your request is waiting for approval.',
  );
});

test('non-command chat does nothing and never touches the api', async () => {
  const { api, calls } = fakeApi();
  assert.equal(await new Bridge(api).handle(say('lol nice')), null);
  assert.equal(calls.submit.length + calls.status.length, 0);
});

test('queue reply includes the persons own positions', async () => {
  const { api } = fakeApi({
    status: async () => ({ acceptingSubmissions: true, queueLength: 4, nowPlaying: null, yourPositions: [2, 4] }),
  });
  assert.equal(await new Bridge(api).handle(say('!queue')), '@Viewer 4 in the queue. You\'re at #2, #4.');
});

test('a closed queue is reported as closed', async () => {
  const { api } = fakeApi({
    status: async () => ({ acceptingSubmissions: false, queueLength: 0, nowPlaying: null, yourPositions: [] }),
  });
  assert.equal(await new Bridge(api).handle(say('!queue')), '@Viewer the queue is closed right now.');
});

test('now playing shows artist and title, or says nothing is playing', async () => {
  const playing = fakeApi({
    status: async () => ({
      acceptingSubmissions: true,
      queueLength: 1,
      nowPlaying: { title: 'Song', artist: 'Band' },
      yourPositions: [],
    }),
  });
  assert.equal(await new Bridge(playing.api).handle(say('!np')), '@Viewer now playing: Band - Song');
  assert.equal(await new Bridge(fakeApi().api).handle(say('!np')), '@Viewer nothing is playing right now.');
});

test('server rejections are relayed to the viewer in plain words', async () => {
  const { api } = fakeApi({
    submit: async () => {
      throw new WebhookApiError(400, 'This track is already in the queue.');
    },
  });
  assert.equal(
    await new Bridge(api).handle(say('!sr https://youtu.be/a')),
    '@Viewer This track is already in the queue.',
  );
});

test('server trouble and network failures get a generic message, not internals', async () => {
  const down = fakeApi({
    submit: async () => {
      throw new WebhookApiError(502, '<html>bad gateway</html>');
    },
  });
  const offline = fakeApi({
    submit: async () => {
      throw new TypeError('fetch failed');
    },
  });
  const expected = "@Viewer the queue isn't reachable right now, try again in a bit.";
  assert.equal(await new Bridge(down.api).handle(say('!sr https://a.test/x')), expected);
  assert.equal(await new Bridge(offline.api).handle(say('!sr https://a.test/x')), expected);
});

test('per-person cooldown swallows spam but not other people', async () => {
  let now = 1_000;
  const { api, calls } = fakeApi();
  const bridge = new Bridge(api, { cooldownMs: 5_000, now: () => now });

  assert.ok(await bridge.handle(say('!queue', 'a')));
  assert.equal(await bridge.handle(say('!queue', 'a')), null); // same person, too soon
  assert.ok(await bridge.handle(say('!queue', 'b'))); // someone else is fine
  now += 5_001;
  assert.ok(await bridge.handle(say('!queue', 'a'))); // cooldown over
  assert.equal(calls.status.length, 3);
});

test('the same id on two platforms is two people', async () => {
  const { api } = fakeApi();
  const bridge = new Bridge(api, { cooldownMs: 60_000 });

  assert.ok(await bridge.handle({ ...say('!queue'), source: 'twitch' }));
  assert.ok(await bridge.handle({ ...say('!queue'), source: 'youtube' }));
});

test('plain chat does not start a cooldown', async () => {
  const { api } = fakeApi();
  const bridge = new Bridge(api, { cooldownMs: 60_000 });

  await bridge.handle(say('hello'));
  assert.ok(await bridge.handle(say('!queue')));
});
