import type { ChatMessage, ChatTransport, MessageHandler } from './chat.ts';

export interface IrcLine {
  tags: Record<string, string>;
  prefix: string | null;
  command: string;
  params: string[];
  trailing: string | null;
}

const TAG_ESCAPES: Record<string, string> = { ':': ';', s: ' ', r: '\r', n: '\n', '\\': '\\' };

function unescapeTag(value: string): string {
  return value.replace(/\\(.)/g, (_, ch: string) => TAG_ESCAPES[ch] ?? ch);
}

/** Parses one IRCv3 line (with Twitch's message tags). Returns null for blank lines. */
export function parseIrcLine(raw: string): IrcLine | null {
  let rest = raw.replace(/[\r\n]+$/, '');
  if (!rest) return null;

  const tags: Record<string, string> = {};
  if (rest.startsWith('@')) {
    const end = rest.indexOf(' ');
    if (end === -1) return null;
    for (const pair of rest.slice(1, end).split(';')) {
      const eq = pair.indexOf('=');
      if (eq === -1) tags[pair] = '';
      else tags[pair.slice(0, eq)] = unescapeTag(pair.slice(eq + 1));
    }
    rest = rest.slice(end + 1);
  }

  let prefix: string | null = null;
  if (rest.startsWith(':')) {
    const end = rest.indexOf(' ');
    if (end === -1) return null;
    prefix = rest.slice(1, end);
    rest = rest.slice(end + 1);
  }

  let trailing: string | null = null;
  const trailingAt = rest.indexOf(' :');
  if (trailingAt !== -1) {
    trailing = rest.slice(trailingAt + 2);
    rest = rest.slice(0, trailingAt);
  }

  const [command = '', ...params] = rest.split(' ').filter(Boolean);
  return { tags, prefix, command: command.toUpperCase(), params, trailing };
}

/** A chat line from a viewer, or null for anything else (joins, notices, our own echoes, no user id). */
export function toChatMessage(line: IrcLine): ChatMessage | null {
  if (line.command !== 'PRIVMSG' || line.trailing === null) return null;
  const userId = line.tags['user-id'];
  if (!userId) return null;

  const nick = line.prefix?.split('!')[0] ?? '';
  return {
    source: 'twitch',
    userId,
    displayName: line.tags['display-name'] || nick || 'viewer',
    text: line.trailing,
  };
}

export interface TwitchOptions {
  channel: string;
  /** With both set the bridge can answer in chat; without them it joins anonymously and only listens. */
  nick?: string | null;
  oauth?: string | null;
  url?: string;
  log?: (line: string) => void;
}

const MIN_SEND_GAP_MS = 1_500;

export class TwitchTransport implements ChatTransport {
  readonly name = 'twitch';

  private readonly options: TwitchOptions;
  private socket: WebSocket | null = null;
  private handler: MessageHandler | null = null;
  private stopped = false;
  private attempt = 0;
  private lastSendAt = 0;
  private sendChain: Promise<void> = Promise.resolve();

  constructor(options: TwitchOptions) {
    this.options = options;
  }

  get canReply(): boolean {
    return Boolean(this.options.nick && this.options.oauth);
  }

  start(onMessage: MessageHandler): void {
    this.handler = onMessage;
    this.stopped = false;
    this.connect();
  }

  stop(): void {
    this.stopped = true;
    this.socket?.close();
    this.socket = null;
  }

  reply(text: string): Promise<void> {
    if (!this.canReply) return Promise.resolve();
    // Twitch drops bursts, so space our own messages out.
    this.sendChain = this.sendChain.then(async () => {
      const wait = this.lastSendAt + MIN_SEND_GAP_MS - Date.now();
      if (wait > 0) await new Promise((resolve) => setTimeout(resolve, wait));
      this.lastSendAt = Date.now();
      this.send(`PRIVMSG #${this.options.channel} :${text.replace(/[\r\n]+/g, ' ').slice(0, 450)}`);
    });
    return this.sendChain;
  }

  private connect(): void {
    const log = this.options.log ?? (() => {});
    const socket = new WebSocket(this.options.url ?? 'wss://irc-ws.chat.twitch.tv:443');
    this.socket = socket;

    socket.addEventListener('open', () => {
      this.attempt = 0;
      const { nick, oauth, channel } = this.options;
      socket.send('CAP REQ :twitch.tv/tags twitch.tv/commands');
      if (nick && oauth) {
        socket.send(`PASS ${oauth.startsWith('oauth:') ? oauth : `oauth:${oauth}`}`);
        socket.send(`NICK ${nick}`);
      } else {
        socket.send(`NICK justinfan${Math.floor(10_000 + Math.random() * 89_999)}`);
      }
      socket.send(`JOIN #${channel}`);
      log(`twitch: joined #${channel}${this.canReply ? '' : ' (read-only)'}`);
    });

    socket.addEventListener('message', (event) => {
      for (const raw of String(event.data).split('\r\n')) {
        const line = parseIrcLine(raw);
        if (!line) continue;

        if (line.command === 'PING') {
          this.send(`PONG :${line.trailing ?? 'tmi.twitch.tv'}`);
        } else if (line.command === 'RECONNECT') {
          socket.close();
        } else {
          const message = toChatMessage(line);
          if (message) void Promise.resolve(this.handler?.(message)).catch((e) => log(`twitch: handler failed: ${e}`));
        }
      }
    });

    socket.addEventListener('close', () => {
      if (this.stopped) return;
      const delay = Math.min(30_000, 1_000 * 2 ** Math.min(this.attempt++, 5));
      log(`twitch: disconnected, retrying in ${Math.round(delay / 1000)}s`);
      setTimeout(() => {
        if (!this.stopped) this.connect();
      }, delay);
    });

    socket.addEventListener('error', () => {
      /* the close handler drives reconnects */
    });
  }

  private send(line: string): void {
    if (this.socket?.readyState === WebSocket.OPEN) this.socket.send(line);
  }
}
