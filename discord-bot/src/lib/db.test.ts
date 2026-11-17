import assert from 'node:assert/strict';
import { test } from 'node:test';
process.env.DISCORD_TOKEN = 'test-only';
process.env.DISCORD_CLIENT_ID = 'test-only';
process.env.DB_PATH = ':memory:';
const { saveLink, getLink, setOpenMessage } = await import('./db.js');

test('pairing another channel refreshes the room token without losing existing channel settings', () => {
  const link = { guildId: 'guild', channelId: 'first', roomId: 'room', botToken: 'old', linkedBy: 'owner', linkedAt: 'now' };
  saveLink(link);
  saveLink({ ...link, channelId: 'other-room', roomId: 'other' });
  setOpenMessage('guild', 'first', 'Welcome back');
  saveLink({ ...link, channelId: 'second', botToken: 'new' });
  assert.equal(getLink('guild', 'first')?.botToken, 'new');
  assert.equal(getLink('guild', 'second')?.botToken, 'new');
  assert.equal(getLink('guild', 'first')?.openMessage, 'Welcome back');
  assert.equal(getLink('guild', 'other-room')?.botToken, 'old');
});
