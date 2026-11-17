import { SlashCommandBuilder } from 'discord.js';
import type { ChatInputCommandInteraction } from 'discord.js';
import { getLink, rememberSubmission } from '../lib/db.js';
import { submitRequest } from '../lib/sqApi.js';
import { fetchOembedMetadata } from '../lib/metadata.js';
import { extractRequestLink } from '../lib/requestLinks.js';
import { submissionReply } from '../lib/queuePresentation.js';

export const data = new SlashCommandBuilder()
  .setName('request')
  .setDescription('Submit a track to the queue.')
  .addStringOption((opt) => opt.setName('url').setDescription('Track link from any supported source').setRequired(true));

export async function execute(interaction: ChatInputCommandInteraction): Promise<void> {
  const link = interaction.guildId ? getLink(interaction.guildId, interaction.channelId) : null;
  if (!link) {
    await interaction.reply({
      content: "This channel isn't linked to a queue yet. Ask a mod to run `/link-queue`.",
      ephemeral: true,
    });
    return;
  }

  const url = extractRequestLink(interaction.options.getString('url', true), true);
  if (!url) { await interaction.reply({ content: 'Enter a track link.', ephemeral: true }); return; }
  await interaction.deferReply({ ephemeral: true });
  try {
    const metadata = await fetchOembedMetadata(url);
    const result = await submitRequest(link.roomId, interaction.user.id, interaction.user.displayName, url, 'normal', metadata, interaction.channelId);
    rememberSubmission(interaction.user.id, interaction.guildId!, interaction.channelId, result.submissionId);
    await interaction.editReply(submissionReply(result));
  } catch (err) {
    await interaction.editReply(`Couldn't add that (${(err as Error).message}).`);
  }
}
