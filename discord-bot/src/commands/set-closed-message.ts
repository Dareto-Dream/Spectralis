import { PermissionFlagsBits, SlashCommandBuilder } from 'discord.js';
import type { ChatInputCommandInteraction } from 'discord.js';
import { getLink, setClosedMessage } from '../lib/db.js';

export const data = new SlashCommandBuilder()
  .setName('set-closed-message')
  .setDescription('Customize the message posted when the queue closes (leave blank to reset to default).')
  .addStringOption((opt) => opt.setName('message').setDescription('New closed message').setRequired(false))
  .setDefaultMemberPermissions(PermissionFlagsBits.ManageChannels);

export async function execute(interaction: ChatInputCommandInteraction): Promise<void> {
  const link = interaction.guildId ? getLink(interaction.guildId, interaction.channelId) : null;
  if (!link) {
    await interaction.reply({ content: "This channel isn't linked to a queue yet.", ephemeral: true });
    return;
  }

  const message = interaction.options.getString('message');
  setClosedMessage(link.guildId, link.channelId, message);
  await interaction.reply({
    content: message ? `✅ Closed message set to: ${message}` : '✅ Reset to the default closed message.',
    ephemeral: true,
  });
}
