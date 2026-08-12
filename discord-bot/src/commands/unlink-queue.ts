import { PermissionFlagsBits, SlashCommandBuilder } from 'discord.js';
import type { ChatInputCommandInteraction } from 'discord.js';
import { getLink, removeLink } from '../lib/db.js';
import { stopPolling } from '../lib/nowPlayingPoller.js';

export const data = new SlashCommandBuilder()
  .setName('unlink-queue')
  .setDescription('Unlink this channel from its Streamer Queue room.')
  .setDefaultMemberPermissions(PermissionFlagsBits.ManageChannels);

export async function execute(interaction: ChatInputCommandInteraction): Promise<void> {
  if (!interaction.guildId) {
    await interaction.reply({ content: 'This command only works in a server channel.', ephemeral: true });
    return;
  }
  const link = getLink(interaction.guildId, interaction.channelId);
  const removed = removeLink(interaction.guildId, interaction.channelId);
  if (link) stopPolling(link);
  await interaction.reply({
    content: removed ? '✅ Unlinked this channel.' : 'This channel wasn\'t linked to a queue.',
    ephemeral: true,
  });
}
