import { config } from './config.js';
import type { SqPromoteResponse, SqPublicRoom, SqSubmitResponse } from '../types.js';

const BASE = config.sqApiBaseUrl;

class SqApiError extends Error {
  constructor(
    public status: number,
    message: string,
  ) {
    super(message);
  }
}

async function request<T>(path: string, init?: RequestInit): Promise<T> {
  const res = await fetch(`${BASE}${path}`, {
    ...init,
    headers: { 'content-type': 'application/json', ...init?.headers },
  });
  if (!res.ok) {
    const body = await res.text().catch(() => '');
    throw new SqApiError(res.status, body || `SQ API error ${res.status}`);
  }
  return (await res.json()) as T;
}

/** Fingerprint fields the bot sends on every viewer-facing call. A Discord user ID
 * as the cookie gives the backend's per-person rate limiting something stable to key
 * on, same as the browser cookie sq.js sets. See build_fingerprint in main.rs. */
function fingerprintFor(discordUserId: string) {
  return {
    fpCookie: `discord:${discordUserId}`,
    fpUa: 'discord-bot',
    fpScreen: '',
    fpTz: 0,
  };
}

export async function getRoom(roomId: string): Promise<SqPublicRoom> {
  return request<SqPublicRoom>(`/streamer-queue/v1/rooms/${encodeURIComponent(roomId)}`);
}

export async function submitRequest(
  roomId: string,
  discordUserId: string,
  displayName: string,
  url: string,
  tier: 'normal' | 'skip' | 'super_skip' = 'normal',
): Promise<SqSubmitResponse> {
  return request<SqSubmitResponse>(`/streamer-queue/v1/rooms/${encodeURIComponent(roomId)}/submit`, {
    method: 'POST',
    body: JSON.stringify({ url, displayName, tier, ...fingerprintFor(discordUserId) }),
  });
}

export async function promoteSubmission(
  roomId: string,
  submissionId: string,
  discordUserId: string,
  displayName: string,
  tier: 'skip' | 'super_skip',
): Promise<SqPromoteResponse> {
  return request<SqPromoteResponse>(
    `/streamer-queue/v1/rooms/${encodeURIComponent(roomId)}/submissions/${encodeURIComponent(submissionId)}/promote`,
    {
      method: 'POST',
      body: JSON.stringify({ tier, displayName, ...fingerprintFor(discordUserId) }),
    },
  );
}

// ── Discord pairing / scoped bot token ───────────────────────────────────────────
// See PLANNING.md — exchangeDiscordPin backs /link-queue, setAcceptingSubmissions
// backs /close-queue and /open-queue via the bot token minted by the exchange.

export async function exchangeDiscordPin(pin: string): Promise<{ roomId: string; botToken: string }> {
  return request(`/streamer-queue/v1/rooms/discord-pin/exchange`, {
    method: 'POST',
    body: JSON.stringify({ pin }),
  });
}

export async function setAcceptingSubmissions(roomId: string, botToken: string, accepting: boolean): Promise<void> {
  await request(`/streamer-queue/v1/rooms/${encodeURIComponent(roomId)}/settings`, {
    method: 'PUT',
    body: JSON.stringify({ botToken, acceptingSubmissions: accepting }),
  });
}
