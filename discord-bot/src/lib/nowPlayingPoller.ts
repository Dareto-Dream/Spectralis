import type { Client, SendableChannels } from 'discord.js';
import { EmbedBuilder } from 'discord.js';
import { getRoom } from './sqApi.js';
import type { GuildLink } from '../types.js';

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
  const room = await getRoom(link.roomId).catch(() => null);
  if (!room) return;

  const key = keyFor(link);
  const prev = state.get(key) ?? { lastNowPlayingId: null, pinnedMessageId: null };
  if (room.nowPlayingId === prev.lastNowPlayingId) return;

  const channel = await client.channels.fetch(link.channelId).catch(() => null);
  if (!channel || !channel.isSendable()) return;

  const embed = buildNowPlayingEmbed(room);
  await postOrEditNowPlaying(channel, prev, embed);

  state.set(key, { ...prev, lastNowPlayingId: room.nowPlayingId });
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
