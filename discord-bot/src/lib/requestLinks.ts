const SOURCES = ['spotify.com', 'spotify.link', 'youtu.be', 'youtube.com', 'soundcloud.com', 'suno.com', 'suno.ai', 'bandlab.com', 'untitled.stream', 'bandcamp.com'];
const AUDIO = /\.(mp3|wav|flac|m4a|aac|ogg|opus|webm|aiff|aif|wma)$/i;

/** Ignore ordinary chat links; an explicit /request may use any HTTP media source. */
export function extractRequestLink(text: string, allowGeneric = false): string | null {
  for (const match of text.matchAll(/(?:https?:\/\/|spotify:(?:track|album|playlist):)[^\s<>"`]+/gi)) {
    let candidate = match[0].replace(/[.,!;:'\]]+$/, '');
    while (candidate.endsWith(')') && (candidate.match(/\)/g)?.length ?? 0) > (candidate.match(/\(/g)?.length ?? 0)) candidate = candidate.slice(0, -1);
    if (/^spotify:(track|album|playlist):[A-Za-z0-9]{22}$/.test(candidate)) return candidate;
    try {
      const url = new URL(candidate);
      if (!['https:', 'http:'].includes(url.protocol) || url.username || url.password) continue;
      if (allowGeneric || AUDIO.test(url.pathname) || SOURCES.some(host => url.hostname === host || url.hostname.endsWith('.' + host))) return url.href;
    } catch { /* Keep looking for a supported link in the message. */ }
  }
  return null;
}
