import assert from 'node:assert/strict';
import { test } from 'node:test';
import { loadConfig } from './config.ts';

const base = { SQ_API_BASE_URL: 'https://api.example.test', SQ_ROOM_ID: 'room1', SQ_WEBHOOK_KEY: 'wk_x' };

test('twitch only, anonymous and read-only', () => {
  const config = loadConfig({ ...base, TWITCH_CHANNEL: '#Streamer' });

  assert.deepEqual(config.twitch, { channel: 'streamer', nick: null, oauth: null });
  assert.equal(config.youtube, null);
  assert.equal(config.prefix, '!');
  assert.equal(config.cooldownMs, 5000);
});

test('both platforms with tuning', () => {
  const config = loadConfig({
    ...base,
    TWITCH_CHANNEL: 'streamer',
    TWITCH_NICK: 'BotName',
    TWITCH_OAUTH: 'oauth:abc',
    YOUTUBE_API_KEY: 'key',
    YOUTUBE_VIDEO_ID: 'vid',
    CHAT_PREFIX: '?',
    CHAT_COOLDOWN_SECONDS: '2.5',
  });

  assert.deepEqual(config.twitch, { channel: 'streamer', nick: 'botname', oauth: 'oauth:abc' });
  assert.deepEqual(config.youtube, { apiKey: 'key', liveChatId: null, videoId: 'vid' });
  assert.equal(config.prefix, '?');
  assert.equal(config.cooldownMs, 2500);
});

test('reports every problem at once', () => {
  assert.throws(
    () => loadConfig({}),
    (err: Error) =>
      err.message.includes('SQ_API_BASE_URL is required') &&
      err.message.includes('SQ_ROOM_ID is required') &&
      err.message.includes('SQ_WEBHOOK_KEY is required') &&
      err.message.includes('nothing to listen to'),
  );
});

test('an oauth token needs a nick, and youtube needs something to follow', () => {
  assert.throws(
    () => loadConfig({ ...base, TWITCH_CHANNEL: 'c', TWITCH_OAUTH: 'oauth:x' }),
    /TWITCH_NICK is required/,
  );
  assert.throws(() => loadConfig({ ...base, YOUTUBE_API_KEY: 'k' }), /YOUTUBE_LIVE_CHAT_ID or YOUTUBE_VIDEO_ID/);
});

test('a bad cooldown is rejected', () => {
  assert.throws(
    () => loadConfig({ ...base, TWITCH_CHANNEL: 'c', CHAT_COOLDOWN_SECONDS: 'soon' }),
    /CHAT_COOLDOWN_SECONDS/,
  );
});
