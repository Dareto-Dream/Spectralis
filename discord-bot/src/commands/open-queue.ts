import { PermissionFlagsBits, SlashCommandBuilder } from 'discord.js';
import type { ChatInputCommandInteraction } from 'discord.js';
import { getLink } from '../lib/db.js';
import { setAcceptingSubmissions } from '../lib/sqApi.js';
import { unlockChannel } from '../lib/channelLock.js';

const DEFAULT_OPEN_MESSAGE = '---- QUEUE OPEN ----';

export const data = new SlashCommandBuilder()
  .setName('open-queue')
  .setDescription('Resume taking new requests.')
  .setDefaultMemberPermissions(PermissionFlagsBits.ManageChannels);

export async function execute(interaction: ChatInputCommandInteraction): Promise<void> {
  const link = interaction.guildId ? getLink(interaction.guildId, interaction.channelId) : null;
  if (!link) {
    await interaction.reply({ content: "This channel isn't linked to a queue yet.", ephemeral: true });
    return;
  }
  if (!link.botToken) {
    await interaction.reply({
      content: 'This link is missing a bot token — run `/link-queue` again with a fresh PIN from the app.',
      ephemeral: true,
    });
    return;
  }

  await interaction.deferReply({ ephemeral: true });
  try {
    await setAcceptingSubmissions(link.roomId, link.botToken, true);
    await unlockChannel(interaction.client, link.guildId, link.channelId);
    if (interaction.channel?.isSendable()) {
      await interaction.channel.send(link.openMessage ?? DEFAULT_OPEN_MESSAGE);
    }
    await interaction.editReply('✅ Queue reopened for new requests.');
  } catch (err) {
    await interaction.editReply(`Couldn't reopen the queue (${(err as Error).message}).`);
  }
}
