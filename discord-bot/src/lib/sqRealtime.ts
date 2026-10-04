// Client for the Streamer Queue realtime socket (docs/realtime-protocol.md). The bot still polls as a
// safety net; this just tells it the moment a room changes so reactions and the now-playing embed
// update right away instead of up to 15 seconds later.

export const REALTIME_PROTOCOL = 2;
const BOT_VERSION = '0.1.0';

export type FrameMeaning =
  | { kind: 'changed' }
  | { kind: 'update-required'; message: string }
  | { kind: 'ignore' };

export function helloFrame(): string {
  return JSON.stringify({
    v: REALTIME_PROTOCOL,
    t: 'hello',
    proto: REALTIME_PROTOCOL,
    client: { kind: 'bot', version: BOT_VERSION },
    features: [],
  });
}

export function socketUrl(baseUrl: string, roomId: string): string {
  const url = new URL(`/streamer-queue/v2/rooms/${encodeURIComponent(roomId)}/socket`, baseUrl);
  url.protocol = url.protocol === 'http:' ? 'ws:' : 'wss:';
  return url.toString();
}

/** What a server frame means to the bot. Anything unknown is ignored so newer servers stay compatible. */
export function interpretFrame(raw: string): FrameMeaning {
  let frame: { t?: unknown; code?: unknown; message?: unknown };
  try {
    frame = JSON.parse(raw);
  } catch {
    return { kind: 'ignore' };
  }
  if (frame.t === 'sq.changed') return { kind: 'changed' };
  if (frame.t === 'error' && frame.code === 'update_required') {
    return { kind: 'update-required', message: typeof frame.message === 'string' ? frame.message : 'Update required.' };
  }
  return { kind: 'ignore' };
}

/** The slice of WebSocket the client uses, so tests can fake it. */
export interface SocketLike {
  send(data: string): void;
  close(): void;
  addEventListener(type: 'open' | 'close' | 'error', listener: () => void): void;
  addEventListener(type: 'message', listener: (event: { data: unknown }) => void): void;
}

export interface RoomSocketOptions {
  baseUrl: string;
  roomId: string;
  /** Called (debounced) whenever the room reports a change. */
  onChanged: () => void;
  log?: (line: string) => void;
  debounceMs?: number;
  createSocket?: (url: string) => SocketLike;
}

export class RoomSocket {
  private readonly options: RoomSocketOptions;
  private socket: SocketLike | null = null;
  private stopped = true;
  private retired = false;
  private attempt = 0;
  private debounce: ReturnType<typeof setTimeout> | null = null;
  private retry: ReturnType<typeof setTimeout> | null = null;

  constructor(options: RoomSocketOptions) {
    this.options = options;
  }

  /** True once the server told us this bot is too old; it will never reconnect. */
  get isRetired(): boolean {
    return this.retired;
  }

  start(): void {
    this.stopped = false;
    this.connect();
  }

  stop(): void {
    this.stopped = true;
    if (this.debounce) clearTimeout(this.debounce);
    if (this.retry) clearTimeout(this.retry);
    this.debounce = this.retry = null;
    this.socket?.close();
    this.socket = null;
  }

  private connect(): void {
    const log = this.options.log ?? (() => {});
    const url = socketUrl(this.options.baseUrl, this.options.roomId);
    const socket = this.options.createSocket ? this.options.createSocket(url) : (new WebSocket(url) as unknown as SocketLike);
    this.socket = socket;

    socket.addEventListener('open', () => {
      this.attempt = 0;
      socket.send(helloFrame());
    });

    socket.addEventListener('message', (event) => {
      const meaning = interpretFrame(String(event.data));
      if (meaning.kind === 'changed') {
        this.scheduleChanged();
      } else if (meaning.kind === 'update-required') {
        // Reconnecting can't fix an outdated bot, so say so loudly once and stay quiet.
        this.retired = true;
        log(`realtime: ${meaning.message} Falling back to polling only.`);
        this.stop();
      }
    });

    socket.addEventListener('close', () => {
      if (this.stopped || this.socket !== socket) return;
      const delay = Math.min(30_000, 1_000 * 2 ** Math.min(this.attempt++, 5));
      this.retry = setTimeout(() => {
        if (!this.stopped) this.connect();
      }, delay);
    });

    socket.addEventListener('error', () => {
      /* the close handler drives reconnects */
    });
  }

  // A burst of writes (approve, reorder, now-playing) becomes one refresh.
  private scheduleChanged(): void {
    if (this.debounce) return;
    this.debounce = setTimeout(() => {
      this.debounce = null;
      this.options.onChanged();
    }, this.options.debounceMs ?? 400);
  }
}
