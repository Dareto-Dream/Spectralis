export interface BridgeConfig {
  apiBaseUrl: string;
  roomId: string;
  webhookKey: string;
  prefix: string;
  cooldownMs: number;
  twitch: { channel: string; nick: string | null; oauth: string | null } | null;
  youtube: { apiKey: string; liveChatId: string | null; videoId: string | null } | null;
}

type Env = Record<string, string | undefined>;

/** Reads the bridge's settings from environment variables, failing loudly on anything missing. */
export function loadConfig(env: Env): BridgeConfig {
  const problems: string[] = [];
  const need = (name: string): string => {
    const value = env[name]?.trim();
    if (!value) problems.push(`${name} is required`);
    return value ?? '';
  };

  const apiBaseUrl = need('SQ_API_BASE_URL');
  const roomId = need('SQ_ROOM_ID');
  const webhookKey = need('SQ_WEBHOOK_KEY');

  const twitchChannel = env.TWITCH_CHANNEL?.trim().replace(/^#/, '').toLowerCase() || null;
  const twitchOauth = env.TWITCH_OAUTH?.trim() || null;
  const twitchNick = env.TWITCH_NICK?.trim().toLowerCase() || null;
  if (twitchOauth && !twitchNick) problems.push('TWITCH_NICK is required when TWITCH_OAUTH is set');

  const youtubeKey = env.YOUTUBE_API_KEY?.trim() || null;
  const liveChatId = env.YOUTUBE_LIVE_CHAT_ID?.trim() || null;
  const videoId = env.YOUTUBE_VIDEO_ID?.trim() || null;
  if (youtubeKey && !liveChatId && !videoId) {
    problems.push('YOUTUBE_LIVE_CHAT_ID or YOUTUBE_VIDEO_ID is required when YOUTUBE_API_KEY is set');
  }

  if (!twitchChannel && !youtubeKey) problems.push('set TWITCH_CHANNEL and/or YOUTUBE_API_KEY (nothing to listen to)');

  const cooldown = Number(env.CHAT_COOLDOWN_SECONDS ?? '5');
  if (!Number.isFinite(cooldown) || cooldown < 0) problems.push('CHAT_COOLDOWN_SECONDS must be a number >= 0');

  if (problems.length) throw new Error(`Invalid chat-bridge configuration:\n- ${problems.join('\n- ')}`);

  return {
    apiBaseUrl,
    roomId,
    webhookKey,
    prefix: env.CHAT_PREFIX?.trim() || '!',
    cooldownMs: Math.round(cooldown * 1000),
    twitch: twitchChannel ? { channel: twitchChannel, nick: twitchNick, oauth: twitchOauth } : null,
    youtube: youtubeKey ? { apiKey: youtubeKey, liveChatId, videoId } : null,
  };
}
