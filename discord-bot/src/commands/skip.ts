import { SlashCommandBuilder } from 'discord.js';
import type { ChatInputCommandInteraction } from 'discord.js';
import { executePromote } from '../lib/promoteCommand.js';

export const data = new SlashCommandBuilder().setName('skip').setDescription('Bump your last request up the priority tier.');

export async function execute(interaction: ChatInputCommandInteraction): Promise<void> {
  await executePromote(interaction, 'skip', 'Priority bump');
}
