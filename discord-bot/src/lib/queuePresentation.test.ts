import assert from 'node:assert/strict';
import { test } from 'node:test';
import { queuePresentation, submissionReply, terminalReaction } from './queuePresentation.ts';
import type { SqPublicRoom, SqSubmitResponse } from '../types.js';

test('approval and payment statuses do not claim a request is ready to play', () => {
  const result = { status: 'pending', position: null } as SqSubmitResponse;
  assert.match(submissionReply(result), /waiting for the host/);
  assert.match(submissionReply({ ...result, status: 'awaiting_payment' }), /needs payment/);
  assert.match(submissionReply({ ...result, status: 'queued', position: 3 }), /#3/);
});

test('queue presentation changes even when the playing track stays the same', () => {
  const room = { enabled: true, acceptingSubmissions: true, queueLength: 2, nowPlayingId: 'track', nowPlayingTitle: 'Song', nowPlayingArtist: 'Artist' } as SqPublicRoom;
  const original = queuePresentation(room);
  assert.notDeepEqual(queuePresentation({ ...room, queueLength: 3 }), original);
  assert.notDeepEqual(queuePresentation({ ...room, acceptingSubmissions: false }), original);
  assert.match(queuePresentation({ enabled: false } as SqPublicRoom).footer, /Session offline/);
  assert.equal(queuePresentation({ ...room, nowPlayingTitle: 'x'.repeat(300) }).title.length, 256);
});

test('skipped tracks finish their reaction lifecycle without being marked played', () => {
  assert.equal(terminalReaction('skipped'), '⚠️');
  assert.equal(terminalReaction('played'), '✅');
  assert.equal(terminalReaction('pending'), null);
  assert.equal(terminalReaction('playing'), null);
});
