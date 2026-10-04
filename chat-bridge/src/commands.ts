export type ChatCommand =
  | { kind: 'request'; url: string }
  | { kind: 'usage' }
  | { kind: 'queue' }
  | { kind: 'song' };

const REQUEST_ALIASES = new Set(['request', 'sr', 'songrequest']);
const QUEUE_ALIASES = new Set(['queue', 'q']);
const SONG_ALIASES = new Set(['song', 'np', 'nowplaying']);

const LINK = /^(https?:\/\/\S+|spotify:\S+)$/i;

/**
 * Turns a chat line into a command, or null when it isn't one. Chat is mostly small talk, so
 * anything that doesn't start with the prefix and a known word is silently ignored.
 */
export function parseCommand(text: string, prefix = '!'): ChatCommand | null {
  const trimmed = text.trim();
  if (!trimmed.startsWith(prefix)) return null;

  const [head = '', ...rest] = trimmed.slice(prefix.length).split(/\s+/);
  const word = head.toLowerCase();

  if (REQUEST_ALIASES.has(word)) {
    const link = rest.find((token) => LINK.test(token));
    return link ? { kind: 'request', url: link } : { kind: 'usage' };
  }
  if (QUEUE_ALIASES.has(word)) return { kind: 'queue' };
  if (SONG_ALIASES.has(word)) return { kind: 'song' };
  return null;
}
