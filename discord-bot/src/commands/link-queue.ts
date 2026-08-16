import { PermissionFlagsBits, SlashCommandBuilder } from 'discord.js';
import type { ChatInputCommandInteraction } from 'discord.js';
import { saveLink } from '../lib/db.js';
import { startPolling } from '../lib/nowPlayingPoller.js';
import { exchangeDiscordPin } from '../lib/sqApi.js';

export const data = new SlashCommandBuilder()
  .setName('link-queue')
  .setDescription("Link this channel to a Streamer Queue room using the PIN shown in the Spectralis app.")
  .addStringOption((opt) =>
    opt.setName('pin').setDescription('6-digit PIN from the app\'s "Link Discord" button').setRequired(true),
  )
  .setDefaultMemberPermissions(PermissionFlagsBits.ManageChannels);

export async function execute(interaction: ChatInputCommandInteraction): Promise<void> {
  const pin = interaction.options.getString('pin', true);
  if (!interaction.guildId) {
    await interaction.reply({ content: 'This command only works in a server channel.', ephemeral: true });
    return;
  }

  await interaction.deferReply({ ephemeral: true });
  try {
    const { roomId, botToken } = await exchangeDiscordPin(pin);
    const link = {
      guildId: interaction.guildId,
      channelId: interaction.channelId,
      roomId,
      botToken,
      linkedBy: interaction.user.id,
      linkedAt: new Date().toISOString(),
    };
    saveLink(link);
    startPolling(interaction.client, { ...link, openMessage: null, closedMessage: null });
    await interaction.editReply(`✅ Linked this channel to queue \`${roomId}\`. Now-playing updates will start posting here.`);
  } catch (err) {
    await interaction.editReply(`Couldn't link that PIN (${(err as Error).message}).`);
  }
}
