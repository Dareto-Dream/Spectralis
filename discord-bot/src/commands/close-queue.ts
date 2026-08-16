import { PermissionFlagsBits, SlashCommandBuilder } from 'discord.js';
import type { ChatInputCommandInteraction } from 'discord.js';
import { getLink } from '../lib/db.js';
import { setAcceptingSubmissions } from '../lib/sqApi.js';
import { lockChannel } from '../lib/channelLock.js';

const DEFAULT_CLOSED_MESSAGE = '---- QUEUE CLOSED ----';

export const data = new SlashCommandBuilder()
  .setName('close-queue')
  .setDescription('Stop taking new requests — the queue stays visible and playable.')
  .setDefaultMemberPermissions(PermissionFlagsBits.ManageChannels);

export async function execute(interaction: ChatInputCommandInteraction): Promise<void> {
  const link = interaction.guildId ? getLink(interaction.guildId, interaction.channelId) : null;
  if (!link) {
    await interaction.reply({ content: "This channel isn't linked to a queue yet.", ephemeral: true });
    return;
  }
  if (!link.botToken) {
    // Links created before bot tokens existed (or via a partial/manual insert) won't
    // have one — re-linking with a fresh PIN mints one.
    await interaction.reply({
      content: 'This link is missing a bot token — run `/link-queue` again with a fresh PIN from the app.',
      ephemeral: true,
    });
    return;
  }

  await interaction.deferReply({ ephemeral: true });
  try {
    await setAcceptingSubmissions(link.roomId, link.botToken, false);
    await lockChannel(interaction.client, link.guildId, link.channelId);
    if (interaction.channel?.isSendable()) {
      await interaction.channel.send(link.closedMessage ?? DEFAULT_CLOSED_MESSAGE);
    }
    await interaction.editReply('✅ Queue closed to new requests. Existing tracks are still playable.');
  } catch (err) {
    await interaction.editReply(`Couldn't close the queue (${(err as Error).message}).`);
  }
}
