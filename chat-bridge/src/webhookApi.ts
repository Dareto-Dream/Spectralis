export interface SubmitResult {
  submissionId: string;
  status: string;
  position?: number;
}

export interface QueueStatus {
  acceptingSubmissions: boolean;
  queueLength: number;
  nowPlaying: { title?: string | null; artist?: string | null } | null;
  yourPositions: number[];
}

export class WebhookApiError extends Error {
  readonly status: number;

  constructor(status: number, message: string) {
    super(message);
    this.name = 'WebhookApiError';
    this.status = status;
  }
}

type FetchLike = typeof fetch;

/** Client for docs/streamer-queue-webhooks.md. */
export class WebhookApi {
  private readonly base: string;
  private readonly roomId: string;
  private readonly key: string;
  private readonly fetchImpl: FetchLike;

  constructor(baseUrl: string, roomId: string, key: string, fetchImpl: FetchLike = fetch) {
    this.base = baseUrl.replace(/\/+$/, '');
    this.roomId = roomId;
    this.key = key;
    this.fetchImpl = fetchImpl;
  }

  submit(input: {
    source: string;
    userId: string;
    displayName: string;
    url: string;
    title?: string;
    artist?: string;
  }): Promise<SubmitResult> {
    return this.post<SubmitResult>('submit', input);
  }

  status(person: { source: string; userId: string }): Promise<QueueStatus> {
    return this.post<QueueStatus>('status', person);
  }

  private async post<T>(action: string, body: unknown): Promise<T> {
    const res = await this.fetchImpl(
      `${this.base}/streamer-queue/v2/rooms/${encodeURIComponent(this.roomId)}/webhook/${action}`,
      {
        method: 'POST',
        headers: { 'content-type': 'application/json', 'x-spectralis-webhook-key': this.key },
        body: JSON.stringify(body),
        signal: AbortSignal.timeout(10_000),
      },
    );

    if (!res.ok) throw new WebhookApiError(res.status, await readError(res));
    return (await res.json()) as T;
  }
}

async function readError(res: Response): Promise<string> {
  const text = await res.text().catch(() => '');
  try {
    const parsed = JSON.parse(text) as { error?: unknown };
    if (typeof parsed.error === 'string' && parsed.error) return parsed.error;
  } catch {
    // not JSON, fall through
  }
  return text || `Request failed (${res.status})`;
}
