import { Bridge } from './bridge.ts';
import type { ChatTransport } from './chat.ts';
import { loadConfig } from './config.ts';
import { TwitchTransport } from './twitch.ts';
import { WebhookApi } from './webhookApi.ts';
import { YouTubeTransport } from './youtube.ts';

const log = (line: string) => console.log(`${new Date().toISOString()} ${line}`);

const config = loadConfig(process.env);
const api = new WebhookApi(config.apiBaseUrl, config.roomId, config.webhookKey);
const bridge = new Bridge(api, { prefix: config.prefix, cooldownMs: config.cooldownMs });

const transports: ChatTransport[] = [];
if (config.twitch) transports.push(new TwitchTransport({ ...config.twitch, log }));
if (config.youtube) transports.push(new YouTubeTransport({ ...config.youtube, log }));

for (const transport of transports) {
  transport.start(async (message) => {
    const reply = await bridge.handle(message);
    if (!reply) return;
    log(`${transport.name}: ${message.displayName}: ${message.text} -> ${reply}`);
    await transport.reply?.(reply);
  });
}

log(`chat-bridge up: ${transports.map((t) => t.name).join(', ')} -> room ${config.roomId}`);

function shutdown() {
  for (const transport of transports) transport.stop();
  process.exit(0);
}
process.on('SIGINT', shutdown);
process.on('SIGTERM', shutdown);
