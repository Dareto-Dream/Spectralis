import assert from 'node:assert/strict';
import { test } from 'node:test';
import { parseIrcLine, toChatMessage } from './twitch.ts';

const PRIVMSG =
  '@badge-info=;color=#FF0000;display-name=Some\\sViewer;mod=0;user-id=4242 ' +
  ':someviewer!someviewer@someviewer.tmi.twitch.tv PRIVMSG #streamer :!sr https://youtu.be/abc';

test('parses tags, prefix, command, params and trailing text', () => {
  const line = parseIrcLine(PRIVMSG);

  assert.ok(line);
  assert.equal(line.command, 'PRIVMSG');
  assert.deepEqual(line.params, ['#streamer']);
  assert.equal(line.trailing, '!sr https://youtu.be/abc');
  assert.equal(line.prefix, 'someviewer!someviewer@someviewer.tmi.twitch.tv');
  assert.equal(line.tags['user-id'], '4242');
  assert.equal(line.tags['display-name'], 'Some Viewer'); // \s unescaped
  assert.equal(line.tags['badge-info'], '');
});

test('tag escapes round trip', () => {
  const line = parseIrcLine('@a=x\\:y\\\\z\\sw PING :tmi.twitch.tv');
  assert.equal(line?.tags.a, 'x;y\\z w');
});

test('PING keeps its trailing token', () => {
  const line = parseIrcLine('PING :tmi.twitch.tv\r\n');
  assert.equal(line?.command, 'PING');
  assert.equal(line?.trailing, 'tmi.twitch.tv');
});

test('blank and malformed lines are skipped, not thrown on', () => {
  assert.equal(parseIrcLine(''), null);
  assert.equal(parseIrcLine('\r\n'), null);
  assert.equal(parseIrcLine('@tags-only'), null);
});

test('a viewer message becomes a ChatMessage keyed on the stable user id', () => {
  const message = toChatMessage(parseIrcLine(PRIVMSG)!);

  assert.deepEqual(message, {
    source: 'twitch',
    userId: '4242',
    displayName: 'Some Viewer',
    text: '!sr https://youtu.be/abc',
  });
});

test('display name falls back to the nick when the tag is empty', () => {
  const line = parseIrcLine('@user-id=1;display-name= :bob!bob@bob PRIVMSG #c :hi')!;
  assert.equal(toChatMessage(line)?.displayName, 'bob');
});

test('non-chat lines and lines without a user id are not messages', () => {
  assert.equal(toChatMessage(parseIrcLine(':tmi.twitch.tv 001 justinfan1 :Welcome, GLHF!')!), null);
  assert.equal(toChatMessage(parseIrcLine(':a!a@a JOIN #streamer')!), null);
  assert.equal(toChatMessage(parseIrcLine(':a!a@a PRIVMSG #streamer :no tags here')!), null);
});
