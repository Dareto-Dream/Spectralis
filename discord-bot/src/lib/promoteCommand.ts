import type { ChatInputCommandInteraction } from 'discord.js';
import { getLink, lastSubmissionId } from './db.js';
import { promoteSubmission } from './sqApi.js';

/** Shared body for /skip and /superskip — both promote the caller's most recent
 * request in this channel to a priority tier. */
export async function executePromote(
  interaction: ChatInputCommandInteraction,
  tier: 'skip' | 'super_skip',
  label: string,
): Promise<void> {
  if (!interaction.guildId) {
    await interaction.reply({ content: 'This command only works in a server channel.', ephemeral: true });
    return;
  }

  const link = getLink(interaction.guildId, interaction.channelId);
  if (!link) {
    await interaction.reply({
      content: "This channel isn't linked to a queue yet. Ask a mod to run `/link-queue`.",
      ephemeral: true,
    });
    return;
  }

  const submissionId = lastSubmissionId(interaction.user.id, interaction.guildId, interaction.channelId);
  if (!submissionId) {
    await interaction.reply({ content: 'Use `/request` first, then you can skip your own track.', ephemeral: true });
    return;
  }

  await interaction.deferReply({ ephemeral: true });
  try {
    const result = await promoteSubmission(link.roomId, submissionId, interaction.user.id, interaction.user.displayName, tier);
    const position = result.position ? ` — now #${result.position} in line.` : '';
    await interaction.editReply(`✅ ${label} requested${position}`);
  } catch (err) {
    await interaction.editReply(`Couldn't ${label.toLowerCase()} that (${(err as Error).message}).`);
  }
}
