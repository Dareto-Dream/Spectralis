import type { SqPublicRoom, SqSubmitResponse, SqSubmission } from '../types.js';

export function queuePresentation(room: SqPublicRoom) {
  const count = room.queueLength ?? 0;
  const availability = !room.enabled ? 'Session offline' : room.acceptingSubmissions ? 'Requests open' : 'Requests closed';
  return {
    title: (room.nowPlayingId ? room.nowPlayingTitle || 'Untitled' : 'Listening queue').slice(0, 256),
    description: room.nowPlayingId ? room.nowPlayingArtist?.slice(0, 4096) || null : null,
    footer: `${availability} · ${count} in queue`,
    color: room.nowPlayingTier === 'super_skip' ? 0xffcc00 : room.nowPlayingTier === 'skip' ? 0x33aaff : 0xf6b44e,
  };
}

export function submissionReply(result: SqSubmitResponse): string {
  if (result.status === 'pending') return 'Request sent — waiting for the host to approve it.';
  if (result.status === 'awaiting_payment') return 'This request needs payment. Open the session’s request page to complete it.';
  return result.position ? `Added to the queue — you’re #${result.position} in line.` : 'Added to the queue.';
}

export function terminalReaction(status: SqSubmission['status'] | undefined): string | null {
  if (status === 'played') return '✅';
  if (status === undefined || ['skipped', 'rejected', 'payment_failed'].includes(status)) return '⚠️';
  return null;
}
