/** One chat line from any platform, reduced to what the queue cares about. */
export interface ChatMessage {
  /** Lowercase platform slug: "twitch", "youtube", … */
  source: string;
  /** The platform's stable user id (never the display name, which people change). */
  userId: string;
  displayName: string;
  text: string;
}

export type MessageHandler = (message: ChatMessage) => void | Promise<void>;

/** A chat platform the bridge can listen to (and optionally answer on). */
export interface ChatTransport {
  readonly name: string;
  start(onMessage: MessageHandler): void;
  stop(): void;
  /** Absent when the platform connection can't post (e.g. read-only Twitch, YouTube without OAuth). */
  reply?(text: string): Promise<void>;
}
