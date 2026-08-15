import { SlashCommandBuilder } from 'discord.js';
import type { ChatInputCommandInteraction } from 'discord.js';
import { executePromote } from '../lib/promoteCommand.js';

export const data = new SlashCommandBuilder().setName('superskip').setDescription('Send your last request straight to the front.');

export async function execute(interaction: ChatInputCommandInteraction): Promise<void> {
  await executePromote(interaction, 'super_skip', 'Super skip');
}
