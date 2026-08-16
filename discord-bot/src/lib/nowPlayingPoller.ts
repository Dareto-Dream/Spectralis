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

interface ChannelPollState {
  lastNowPlayingId: string | null;
  pinnedMessageId: string | null;
}

const state = new Map<string, ChannelPollState>(); // key: `${guildId}:${channelId}`

function keyFor(link: GuildLink): string {
  return `${link.guildId}:${link.channelId}`;
}

async function pollOnce(client: Client, link: GuildLink): Promise<void> {
  const room = link.botToken
    ? await getRoomAsBot(link.roomId, link.botToken).catch(() => null)
    : await getRoom(link.roomId).catch(() => null);
  if (!room) return;

  if (link.botToken) {
    await reconcileReactions(client, link, (room as SqBotRoom).submissions);
  }

  const key = keyFor(link);
  const prev = state.get(key) ?? { lastNowPlayingId: null, pinnedMessageId: null };
  if (room.nowPlayingId === prev.lastNowPlayingId) return;

  const channel = await client.channels.fetch(link.channelId).catch(() => null);
  if (!channel || !channel.isSendable()) return;

  const embed = buildNowPlayingEmbed(room);
  await postOrEditNowPlaying(channel, prev, embed);

  state.set(key, { ...prev, lastNowPlayingId: room.nowPlayingId });
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
