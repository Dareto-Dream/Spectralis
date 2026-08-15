import { SlashCommandBuilder } from 'discord.js';
import type { ChatInputCommandInteraction } from 'discord.js';
import { getLink, rememberSubmission } from '../lib/db.js';
import { submitRequest } from '../lib/sqApi.js';

export const data = new SlashCommandBuilder()
  .setName('request')
  .setDescription('Submit a track to the queue.')
  .addStringOption((opt) => opt.setName('url').setDescription('Spotify, YouTube, SoundCloud link').setRequired(true));

export async function execute(interaction: ChatInputCommandInteraction): Promise<void> {
  const link = interaction.guildId ? getLink(interaction.guildId, interaction.channelId) : null;
  if (!link) {
    await interaction.reply({
      content: "This channel isn't linked to a queue yet. Ask a mod to run `/link-queue`.",
      ephemeral: true,
    });
    return;
  }

  const url = interaction.options.getString('url', true);
  await interaction.deferReply({ ephemeral: true });
  try {
    const result = await submitRequest(link.roomId, interaction.user.id, interaction.user.displayName, url);
    rememberSubmission(interaction.user.id, interaction.guildId!, interaction.channelId, result.submissionId);
    const position = result.position ? ` — you're #${result.position} in line.` : '';
    await interaction.editReply(`✅ Added to the queue${position}`);
  } catch (err) {
    await interaction.editReply(`Couldn't add that (${(err as Error).message}).`);
  }
}
