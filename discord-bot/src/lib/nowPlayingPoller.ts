import type { Client, SendableChannels } from 'discord.js';
import { EmbedBuilder } from 'discord.js';
import { getRoom, getRoomAsBot } from './sqApi.js';
import { trackedForChannel, untrackSubmission } from './db.js';
import type { GuildLink, SqBotRoom, SqSubmission } from '../types.js';

const SUBMITTED_REACTION = '📥';
const PLAYED_REACTION = '✅';
const MISSED_REACTION = '⚠️';

const POLL_INTERVAL_MS = 15_000;
const STAGGER_MS = 500;
const MAX_BACKOFF_MS = 5 * 60_000;

interface ChannelPollState {
  lastNowPlayingId: string | null;
  pinnedMessageId: string | null;
  consecutiveFailures: number;
  nextAttemptAtMs: number;
}

const state = new Map<string, ChannelPollState>(); // key: `${guildId}:${channelId}`

function keyFor(link: GuildLink): string {
  return `${link.guildId}:${link.channelId}`;
}

function emptyState(): ChannelPollState {
  return { lastNowPlayingId: null, pinnedMessageId: null, consecutiveFailures: 0, nextAttemptAtMs: 0 };
}

async function pollOnce(client: Client, link: GuildLink): Promise<void> {
  const key = keyFor(link);
  const prev = state.get(key) ?? emptyState();

  // A room that's been unreachable for a while (deleted/unlinked upstream) doesn't need
  // hammering every 15s forever — back off with the failure streak, capped at 5 minutes.
  if (Date.now() < prev.nextAttemptAtMs) return;

  const room = link.botToken
    ? await getRoomAsBot(link.roomId, link.botToken).catch(() => null)
    : await getRoom(link.roomId).catch(() => null);

  if (!room) {
    const consecutiveFailures = prev.consecutiveFailures + 1;
    const backoffMs = Math.min(consecutiveFailures * POLL_INTERVAL_MS, MAX_BACKOFF_MS);
    state.set(key, { ...prev, consecutiveFailures, nextAttemptAtMs: Date.now() + backoffMs });
    return;
  }

  try {
    if (link.botToken) {
      await reconcileReactions(client, link, (room as SqBotRoom).submissions);
    }

    const current = state.get(key) ?? prev;
    const cleared = { ...current, consecutiveFailures: 0, nextAttemptAtMs: 0 };
    if (room.nowPlayingId === cleared.lastNowPlayingId) {
      state.set(key, cleared);
      return;
    }

    const channel = await client.channels.fetch(link.channelId).catch(() => null);
    if (!channel || !channel.isSendable()) {
      state.set(key, cleared);
      return;
    }

    const embed = buildNowPlayingEmbed(room);
    await postOrEditNowPlaying(channel, cleared, embed);

    state.set(key, { ...cleared, lastNowPlayingId: room.nowPlayingId });
  } catch (err) {
    // A failed embed post/edit used to throw out of pollOnce as an unhandled rejection and
    // skip the state update, re-posting the same embed every cycle until it happened to work.
    console.error(`Now-playing poll failed for ${key}:`, err);
  }
}

/** Keeps each tracked submission's reaction in sync with its backend status: 📥 while
 * queued/playing, ✅ once it actually played, ⚠️ if it was rejected/failed payment or
 * disappeared from the queue entirely (removed without playing — same bucket as "the
 * person wasn't present", per the streamer's call). */
async function reconcileReactions(client: Client, link: GuildLink, submissions: SqSubmission[]): Promise<void> {
  const tracked = trackedForChannel(link.guildId, link.channelId);
  if (tracked.length === 0) return;

  const byId = new Map(submissions.map((s) => [s.id, s]));

  for (const row of tracked) {
    const sub = byId.get(row.submission_id);
    const terminal = !sub || sub.status === 'played' || sub.status === 'rejected' || sub.status === 'payment_failed';
    if (!terminal) continue;

    const reaction = !sub || sub.status !== 'played' ? MISSED_REACTION : PLAYED_REACTION;
    const channel = await client.channels.fetch(row.channel_id).catch(() => null);
    if (channel && channel.isSendable()) {
      const message = await channel.messages.fetch(row.message_id).catch(() => null);
      if (message) {
        await message.reactions.resolve(SUBMITTED_REACTION)?.users.remove(client.user!.id).catch(() => {});
        await message.react(reaction).catch(() => {});
      }
    }
    untrackSubmission(row.submission_id);
  }
}

function buildNowPlayingEmbed(room: Awaited<ReturnType<typeof getRoom>>) {
  return new EmbedBuilder()
    .setTitle(room.nowPlayingId ? room.nowPlayingTitle ?? 'Untitled' : 'Nothing playing')
    .setDescription(room.nowPlayingArtist ?? null)
    .setFooter({ text: `${room.queueLength} in queue` })
    .setColor(room.nowPlayingTier === 'super_skip' ? 0xffcc00 : room.nowPlayingTier === 'skip' ? 0x33aaff : 0x8899aa);
}

async function postOrEditNowPlaying(
  channel: SendableChannels,
  prev: ChannelPollState,
  embed: EmbedBuilder,
): Promise<void> {
  if (prev.pinnedMessageId) {
    const existing = await channel.messages.fetch(prev.pinnedMessageId).catch(() => null);
    if (existing) {
      await existing.edit({ embeds: [embed] });
      return;
    }
  }
  const sent = await channel.send({ embeds: [embed] });
  prev.pinnedMessageId = sent.id;
}

const timers = new Map<string, NodeJS.Timeout>();

export function startPolling(client: Client, link: GuildLink, indexForStagger = 0): void {
  const key = keyFor(link);
  stopPolling(link);
  const delay = (indexForStagger * STAGGER_MS) % POLL_INTERVAL_MS;
  const timer = setTimeout(function tick() {
    void pollOnce(client, link);
    timers.set(key, setTimeout(tick, POLL_INTERVAL_MS));
  }, delay);
  timers.set(key, timer);
}

export function stopPolling(link: GuildLink): void {
  const key = keyFor(link);
  const timer = timers.get(key);
  if (timer) {
    clearTimeout(timer);
    timers.delete(key);
  }
  state.delete(key);
}
