import type { ChatMessage, ChatTransport, MessageHandler } from './chat.ts';

export interface LiveChatItem {
  snippet?: { type?: string; displayMessage?: string; publishedAt?: string };
  authorDetails?: { channelId?: string; displayName?: string };
}

/**
 * Viewer text messages published at or after `sinceMs`. The first poll of a live chat returns recent
 * history; without the cutoff, restarting the bridge would replay old requests.
 */
export function mapLiveChatItems(items: LiveChatItem[], sinceMs: number): ChatMessage[] {
  const messages: ChatMessage[] = [];
  for (const item of items) {
    if (item.snippet?.type !== 'textMessageEvent') continue;
    const text = item.snippet.displayMessage;
    const userId = item.authorDetails?.channelId;
    if (!text || !userId) continue;

    const published = Date.parse(item.snippet.publishedAt ?? '');
    if (Number.isFinite(published) && published < sinceMs) continue;

    messages.push({
      source: 'youtube',
      userId,
      displayName: item.authorDetails?.displayName || 'viewer',
      text,
    });
  }
  return messages;
}

type FetchLike = typeof fetch;

export interface YouTubeOptions {
  apiKey: string;
  liveChatId?: string | null;
  videoId?: string | null;
  fetchImpl?: FetchLike;
  now?: () => number;
  log?: (line: string) => void;
}

const API = 'https://www.googleapis.com/youtube/v3';
const MIN_POLL_MS = 3_000;
const ERROR_BACKOFF_MS = 30_000;

/**
 * Polls a live chat with the Data API. Read-only: posting to YouTube chat needs OAuth on the
 * channel, which this bridge deliberately doesn't ask for, so replies are not available here.
 * Each poll costs a few quota units; the API tells us how long to wait between polls.
 */
export class YouTubeTransport implements ChatTransport {
  readonly name = 'youtube';

  private readonly options: YouTubeOptions;
  private readonly fetchImpl: FetchLike;
  private readonly now: () => number;
  private timer: ReturnType<typeof setTimeout> | null = null;
  private stopped = true;
  private liveChatId: string | null;
  private pageToken: string | undefined;
  private startedAt = 0;
  private handler: MessageHandler | null = null;

  constructor(options: YouTubeOptions) {
    this.options = options;
    this.fetchImpl = options.fetchImpl ?? fetch;
    this.now = options.now ?? Date.now;
    this.liveChatId = options.liveChatId ?? null;
  }

  start(onMessage: MessageHandler): void {
    this.handler = onMessage;
    this.stopped = false;
    this.startedAt = this.now();
    void this.poll();
  }

  stop(): void {
    this.stopped = true;
    if (this.timer) clearTimeout(this.timer);
    this.timer = null;
  }

  private schedule(ms: number): void {
    if (this.stopped) return;
    this.timer = setTimeout(() => void this.poll(), ms);
  }

  private async poll(): Promise<void> {
    const log = this.options.log ?? (() => {});
    try {
      const chatId = this.liveChatId ?? (await this.resolveLiveChatId());
      if (!chatId) {
        log('youtube: no active live chat yet, will check again');
        return this.schedule(ERROR_BACKOFF_MS);
      }
      this.liveChatId = chatId;

      const params = new URLSearchParams({
        liveChatId: chatId,
        part: 'snippet,authorDetails',
        key: this.options.apiKey,
      });
      if (this.pageToken) params.set('pageToken', this.pageToken);

      const res = await this.fetchImpl(`${API}/liveChat/messages?${params}`, { signal: AbortSignal.timeout(15_000) });
      if (!res.ok) {
        const body = await res.text().catch(() => '');
        if (res.status === 403 && body.includes('liveChatEnded')) {
          log('youtube: the live chat has ended, stopping');
          return this.stop();
        }
        throw new Error(`HTTP ${res.status}`);
      }

      const data = (await res.json()) as {
        items?: LiveChatItem[];
        nextPageToken?: string;
        pollingIntervalMillis?: number;
      };
      this.pageToken = data.nextPageToken;

      for (const message of mapLiveChatItems(data.items ?? [], this.startedAt)) {
        try {
          await this.handler?.(message);
        } catch (e) {
          log(`youtube: handler failed: ${e}`);
        }
      }
      this.schedule(Math.max(MIN_POLL_MS, data.pollingIntervalMillis ?? MIN_POLL_MS));
    } catch (e) {
      log(`youtube: poll failed (${e}), retrying in ${ERROR_BACKOFF_MS / 1000}s`);
      this.schedule(ERROR_BACKOFF_MS);
    }
  }

  private async resolveLiveChatId(): Promise<string | null> {
    if (!this.options.videoId) return null;
    const params = new URLSearchParams({
      part: 'liveStreamingDetails',
      id: this.options.videoId,
      key: this.options.apiKey,
    });
    const res = await this.fetchImpl(`${API}/videos?${params}`, { signal: AbortSignal.timeout(15_000) });
    if (!res.ok) throw new Error(`videos lookup failed: HTTP ${res.status}`);
    const data = (await res.json()) as { items?: { liveStreamingDetails?: { activeLiveChatId?: string } }[] };
    return data.items?.[0]?.liveStreamingDetails?.activeLiveChatId ?? null;
  }
}
