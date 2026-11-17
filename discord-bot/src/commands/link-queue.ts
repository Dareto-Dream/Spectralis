import { PermissionFlagsBits, SlashCommandBuilder } from 'discord.js';
import type { ChatInputCommandInteraction } from 'discord.js';
import { allLinks, saveLink } from '../lib/db.js';
import { startPolling } from '../lib/nowPlayingPoller.js';
import { exchangeDiscordPin } from '../lib/sqApi.js';

export const data = new SlashCommandBuilder()
  .setName('link-queue')
  .setDescription("Connect this channel to requests in a Spectralis session.")
  .addStringOption((opt) =>
    opt.setName('pin').setDescription('6-digit code from Sessions → Discord in Spectralis').setRequired(true),
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
    for (const linked of allLinks().filter(item => item.roomId === roomId)) startPolling(interaction.client, linked);
    await interaction.editReply('Connected to Spectralis. Use `/request` or post a supported track link here. Requests appear in Sessions; `/queue` shows the current status.');
  } catch (err) {
    await interaction.editReply(`Couldn't link that PIN (${(err as Error).message}).`);
  }
}
