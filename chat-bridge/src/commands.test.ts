import assert from 'node:assert/strict';
import { test } from 'node:test';
import { parseCommand } from './commands.ts';

test('request aliases pick out the first link', () => {
  for (const word of ['request', 'sr', 'songrequest', 'SR']) {
    assert.deepEqual(parseCommand(`!${word} https://youtu.be/abc please`), {
      kind: 'request',
      url: 'https://youtu.be/abc',
    });
  }
});

test('spotify uris count as links', () => {
  assert.deepEqual(parseCommand('!sr spotify:track:123'), { kind: 'request', url: 'spotify:track:123' });
});

test('a request without a link asks for one instead of failing silently', () => {
  assert.deepEqual(parseCommand('!sr some song name'), { kind: 'usage' });
  assert.deepEqual(parseCommand('!request'), { kind: 'usage' });
});

test('queue and song commands', () => {
  assert.deepEqual(parseCommand('!queue'), { kind: 'queue' });
  assert.deepEqual(parseCommand('!q'), { kind: 'queue' });
  assert.deepEqual(parseCommand('!np'), { kind: 'song' });
  assert.deepEqual(parseCommand('!nowplaying'), { kind: 'song' });
});

test('everything else in chat is ignored', () => {
  assert.equal(parseCommand('hello chat'), null);
  assert.equal(parseCommand('!unknown https://example.com'), null);
  assert.equal(parseCommand('see https://example.com !sr'), null);
  assert.equal(parseCommand(''), null);
});

test('prefix is configurable', () => {
  assert.deepEqual(parseCommand('?sr https://x.test/a', '?'), { kind: 'request', url: 'https://x.test/a' });
  assert.equal(parseCommand('!sr https://x.test/a', '?'), null);
});

test('whitespace around the command is tolerated', () => {
  assert.deepEqual(parseCommand('  !sr   https://x.test/a  '), { kind: 'request', url: 'https://x.test/a' });
});
