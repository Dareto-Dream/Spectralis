import { Client, Collection, Events, GatewayIntentBits, REST, Routes } from 'discord.js';
import type { ChatInputCommandInteraction, SlashCommandBuilder } from 'discord.js';
import { config } from './lib/config.js';
import { allLinks } from './lib/db.js';
import { startPolling } from './lib/nowPlayingPoller.js';

import * as linkQueue from './commands/link-queue.js';
import * as unlinkQueue from './commands/unlink-queue.js';
import * as queue from './commands/queue.js';
import * as request from './commands/request.js';
import * as skip from './commands/skip.js';
import * as superskip from './commands/superskip.js';
import * as closeQueue from './commands/close-queue.js';
import * as openQueue from './commands/open-queue.js';

interface Command {
  data: SlashCommandBuilder;
  execute: (interaction: ChatInputCommandInteraction) => Promise<void>;
}

const commands: Command[] = [linkQueue, unlinkQueue, queue, request, skip, superskip, closeQueue, openQueue] as Command[];
const commandsByName = new Collection<string, Command>(commands.map((c) => [c.data.name, c]));

async function registerCommands(): Promise<void> {
  const rest = new REST().setToken(config.discordToken);
  await rest.put(Routes.applicationCommands(config.discordClientId), {
    body: commands.map((c) => c.data.toJSON()),
  });
}

const client = new Client({ intents: [GatewayIntentBits.Guilds] });

client.once(Events.ClientReady, (readyClient) => {
  console.log(`Logged in as ${readyClient.user.tag}`);
  const links = allLinks();
  links.forEach((link, i) => startPolling(client, link, i));
  console.log(`Polling ${links.length} linked channel(s).`);
});

client.on(Events.InteractionCreate, async (interaction) => {
  if (!interaction.isChatInputCommand()) return;
  const command = commandsByName.get(interaction.commandName);
  if (!command) return;

  try {
    await command.execute(interaction);
  } catch (err) {
    console.error(`Error in /${interaction.commandName}:`, err);
    const payload = { content: 'Something went wrong running that command.', ephemeral: true };
    if (interaction.deferred || interaction.replied) {
      await interaction.editReply(payload).catch(() => {});
    } else {
      await interaction.reply(payload).catch(() => {});
    }
  }
});

await registerCommands();
await client.login(config.discordToken);
