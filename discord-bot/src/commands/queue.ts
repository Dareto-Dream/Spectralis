import { EmbedBuilder, SlashCommandBuilder } from 'discord.js';
import type { ChatInputCommandInteraction } from 'discord.js';
import { getLink } from '../lib/db.js';
import { getRoom } from '../lib/sqApi.js';
import { queuePresentation } from '../lib/queuePresentation.js';

export const data = new SlashCommandBuilder().setName('queue').setDescription("Show what's playing and the queue status.");

export async function execute(interaction: ChatInputCommandInteraction): Promise<void> {
  const link = interaction.guildId ? getLink(interaction.guildId, interaction.channelId) : null;
  if (!link) {
    await interaction.reply({
      content: "This channel isn't linked to a queue yet. Ask a mod to run `/link-queue`.",
      ephemeral: true,
    });
    return;
  }

  await interaction.deferReply();
  try {
    const room = await getRoom(link.roomId);
    const presentation = queuePresentation(room);
    const embed = new EmbedBuilder()
      .setTitle(presentation.title)
      .setDescription(presentation.description)
      .setColor(presentation.color)
      .setFooter({ text: presentation.footer });
    const content = !room.enabled ? 'The host has not started this session.' : room.acceptingSubmissions ? undefined : 'Requests are closed right now.';
    await interaction.editReply({ content, embeds: [embed] });
  } catch (err) {
    await interaction.editReply(`Couldn't reach the queue (${(err as Error).message}).`);
  }
}
