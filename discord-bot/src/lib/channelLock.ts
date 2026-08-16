import { PermissionFlagsBits } from 'discord.js';
import type { Client } from 'discord.js';

async function fetchTextChannel(client: Client, channelId: string) {
  const channel = await client.channels.fetch(channelId).catch(() => null);
  if (!channel || !('permissionOverwrites' in channel)) return null;
  return channel;
}

/** Denies @everyone SEND_MESSAGES on the linked channel. Requires the bot to hold
 * Manage Channels in that channel (see PLANNING.md — granted at invite time). */
export async function lockChannel(client: Client, guildId: string, channelId: string): Promise<void> {
  const channel = await fetchTextChannel(client, channelId);
  if (!channel) return;
  await channel.permissionOverwrites.edit(guildId, { SendMessages: false }).catch(() => {});
}

/** Clears the SEND_MESSAGES overwrite so it falls back to whatever the role/channel
 * defaults already allow, rather than force-granting (which could over-permission a
 * channel that was intentionally locked down for other reasons). */
export async function unlockChannel(client: Client, guildId: string, channelId: string): Promise<void> {
  const channel = await fetchTextChannel(client, channelId);
  if (!channel) return;
  const everyone = channel.permissionOverwrites.cache.get(guildId);
  if (everyone && everyone.deny.has(PermissionFlagsBits.SendMessages)) {
    await channel.permissionOverwrites.edit(guildId, { SendMessages: null }).catch(() => {});
  }
}
