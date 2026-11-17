import assert from 'node:assert/strict';
import { test } from 'node:test';
import { extractRequestLink } from './requestLinks.ts';

test('finds supported links inside ordinary text and Discord markdown', () => {
  assert.equal(extractRequestLink('try [this](https://suno.com/song/song-id).'), 'https://suno.com/song/song-id');
  assert.equal(extractRequestLink('notes https://example.com then <https://soundcloud.com/artist/track>'), 'https://soundcloud.com/artist/track');
  assert.equal(extractRequestLink('play spotify:track:6GyFP1nfCDB8lbD2bG0Hq9'), 'spotify:track:6GyFP1nfCDB8lbD2bG0Hq9');
});

test('ignores unrelated links and lookalike domains in chat', () => {
  for (const text of ['https://example.com', 'https://youtube.com.evil.test/watch?v=x', 'https://youtube.com@evil.test/track', 'spotify:track:nope', 'plain text']) assert.equal(extractRequestLink(text), null);
});

test('supports direct audio and explicit generic source requests', () => {
  assert.equal(extractRequestLink('https://cdn.example.com/music.flac?token=x'), 'https://cdn.example.com/music.flac?token=x');
  assert.equal(extractRequestLink('https://music.example.com/track', true), 'https://music.example.com/track');
  assert.equal(extractRequestLink('file:///private/song.mp3', true), null);
});
