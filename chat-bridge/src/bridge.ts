import { parseCommand } from './commands.ts';
import type { ChatMessage } from './chat.ts';
import { WebhookApiError } from './webhookApi.ts';
import type { QueueStatus, SubmitResult } from './webhookApi.ts';

/** The slice of WebhookApi the bridge needs, so tests can fake it. */
export interface QueueApi {
  submit(input: { source: string; userId: string; displayName: string; url: string }): Promise<SubmitResult>;
  status(person: { source: string; userId: string }): Promise<QueueStatus>;
}

export interface BridgeOptions {
  prefix?: string;
  /** Minimum gap between two commands from the same person. */
  cooldownMs?: number;
  now?: () => number;
}

/**
 * Platform-independent command handling: chat message in, reply text (or null for "say nothing") out.
 * Twitch and YouTube are just different sources of ChatMessage.
 */
export class Bridge {
  private readonly api: QueueApi;
  private readonly prefix: string;
  private readonly cooldownMs: number;
  private readonly now: () => number;
  private readonly lastCommandAt = new Map<string, number>();

  constructor(api: QueueApi, options: BridgeOptions = {}) {
    this.api = api;
    this.prefix = options.prefix ?? '!';
    this.cooldownMs = options.cooldownMs ?? 5_000;
    this.now = options.now ?? Date.now;
  }

  async handle(message: ChatMessage): Promise<string | null> {
    const command = parseCommand(message.text, this.prefix);
    if (!command) return null;

    const who = `${message.source}:${message.userId}`;
    const at = this.now();
    const last = this.lastCommandAt.get(who);
    if (last !== undefined && at - last < this.cooldownMs) return null; // ignore spam silently
    this.lastCommandAt.set(who, at);
    this.pruneCooldowns(at);

    const mention = `@${message.displayName}`;
    try {
      switch (command.kind) {
        case 'usage':
          return `${mention} usage: ${this.prefix}request <link>`;
        case 'request': {
          const result = await this.api.submit({
            source: message.source,
            userId: message.userId,
            displayName: message.displayName,
            url: command.url,
          });
          return result.status === 'pending_approval'
            ? `${mention} your request is waiting for approval.`
            : `${mention} added to the queue${result.position ? ` at #${result.position}` : ''}.`;
        }
        case 'queue': {
          const status = await this.api.status({ source: message.source, userId: message.userId });
          if (!status.acceptingSubmissions) return `${mention} the queue is closed right now.`;
          const mine = status.yourPositions.length
            ? ` You're at ${status.yourPositions.map((p) => `#${p}`).join(', ')}.`
            : '';
          return `${mention} ${status.queueLength} in the queue.${mine}`;
        }
        case 'song': {
          const status = await this.api.status({ source: message.source, userId: message.userId });
          return status.nowPlaying
            ? `${mention} now playing: ${describeTrack(status.nowPlaying)}`
            : `${mention} nothing is playing right now.`;
        }
      }
    } catch (err) {
      if (err instanceof WebhookApiError && err.status < 500) return `${mention} ${err.message}`;
      return `${mention} the queue isn't reachable right now, try again in a bit.`;
    }
  }

  private pruneCooldowns(at: number): void {
    if (this.lastCommandAt.size < 500) return;
    for (const [who, t] of this.lastCommandAt) {
      if (at - t >= this.cooldownMs) this.lastCommandAt.delete(who);
    }
  }
}

function describeTrack(track: { title?: string | null; artist?: string | null }): string {
  const title = track.title?.trim() || 'Unknown track';
  const artist = track.artist?.trim();
  return artist ? `${artist} - ${title}` : title;
}
