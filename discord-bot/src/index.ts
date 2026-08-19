import { ActivityType, Client, Collection, Events, GatewayIntentBits, Partials, REST, Routes } from 'discord.js';
import type { ChatInputCommandInteraction, SlashCommandBuilder } from 'discord.js';
import { config } from './lib/config.js';
import { allLinks } from './lib/db.js';
import { startPolling } from './lib/nowPlayingPoller.js';
import { handlePotentialSubmission } from './lib/messageSubmission.js';

import * as linkQueue from './commands/link-queue.js';
import * as unlinkQueue from './commands/unlink-queue.js';
import * as queue from './commands/queue.js';
import * as request from './commands/request.js';
import * as skip from './commands/skip.js';
import * as superskip from './commands/superskip.js';
import * as closeQueue from './commands/close-queue.js';
import * as openQueue from './commands/open-queue.js';
import * as setOpenMessage from './commands/set-open-message.js';
import * as setClosedMessage from './commands/set-closed-message.js';

interface Command {
  data: SlashCommandBuilder;
  execute: (interaction: ChatInputCommandInteraction) => Promise<void>;
}

const commands: Command[] = [
  linkQueue,
  unlinkQueue,
  queue,
  request,
  skip,
  superskip,
  closeQueue,
  openQueue,
  setOpenMessage,
  setClosedMessage,
] as Command[];
const commandsByName = new Collection<string, Command>(commands.map((c) => [c.data.name, c]));

async function registerCommands(): Promise<void> {
  const rest = new REST().setToken(config.discordToken);
  await rest.put(Routes.applicationCommands(config.discordClientId), {
    body: commands.map((c) => c.data.toJSON()),
  });
}

// A bad promise anywhere in a command handler, poller tick, or event listener used to take the
// whole bot down with no trace. Log and keep running instead — this process has no supervisor
// that would restart it mid-stream, so staying up beats a silent crash.
process.on('unhandledRejection', (reason) => {
  console.error('Unhandled rejection:', reason);
});
process.on('uncaughtException', (err) => {
  console.error('Uncaught exception:', err);
});

const client = new Client({
  intents: [GatewayIntentBits.Guilds, GatewayIntentBits.GuildMessages, GatewayIntentBits.MessageContent],
  partials: [Partials.Message, Partials.Reaction],
});

// discord.js throws if an 'error'/'shardError' event has zero listeners — without these, a
// gateway hiccup crashes the process the same way an unhandled rejection would.
client.on(Events.Error, (err) => console.error('Client error:', err));
client.on(Events.ShardError, (err) => console.error('Shard error:', err));

client.once(Events.ClientReady, (readyClient) => {
  console.log(`Logged in as ${readyClient.user.tag}`);
  readyClient.user.setPresence({ status: 'online', activities: [{ name: 'streamer queues', type: ActivityType.Watching }] });
  const links = allLinks();
  links.forEach((link, i) => startPolling(client, link, i));
  console.log(`Polling ${links.length} linked channel(s).`);
});

client.on(Events.MessageCreate, (message) => {
  void handlePotentialSubmission(message);
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

// Startup only — a transient Discord API blip here used to kill the process outright.
// Not worth retrying once running: the gateway connection itself reconnects on its own.
async function withStartupRetry(label: string, attempt: () => Promise<void>): Promise<void> {
  const delaysMs = [2000, 4000, 8000];
  for (let i = 0; ; i++) {
    try {
      await attempt();
      return;
    } catch (err) {
      if (i >= delaysMs.length) throw err;
      console.error(`${label} failed, retrying in ${delaysMs[i]}ms:`, err);
      await new Promise((resolve) => setTimeout(resolve, delaysMs[i]));
    }
  }
}

await withStartupRetry('Command registration', registerCommands);
await withStartupRetry('Discord login', () => client.login(config.discordToken).then(() => {}));
