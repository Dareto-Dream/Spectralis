import Database from 'better-sqlite3';
import { mkdirSync } from 'node:fs';
import { dirname } from 'node:path';
import { config } from './config.js';
import type { GuildLink } from '../types.js';

mkdirSync(dirname(config.dbPath), { recursive: true });
const db = new Database(config.dbPath);
db.pragma('journal_mode = WAL');

db.exec(`
  CREATE TABLE IF NOT EXISTS guild_links (
    guild_id   TEXT NOT NULL,
    channel_id TEXT NOT NULL,
    room_id    TEXT NOT NULL,
    bot_token  TEXT,
    linked_by  TEXT NOT NULL,
    linked_at  TEXT NOT NULL,
    PRIMARY KEY (guild_id, channel_id)
  );

  CREATE TABLE IF NOT EXISTS submissions (
    discord_user_id TEXT NOT NULL,
    guild_id        TEXT NOT NULL,
    channel_id      TEXT NOT NULL,
    submission_id   TEXT NOT NULL,
    submitted_at    TEXT NOT NULL,
    PRIMARY KEY (discord_user_id, guild_id, channel_id)
  );
`);

interface GuildLinkRow {
  guild_id: string;
  channel_id: string;
  room_id: string;
  bot_token: string | null;
  linked_by: string;
  linked_at: string;
}

function rowToLink(row: GuildLinkRow): GuildLink {
  return {
    guildId: row.guild_id,
    channelId: row.channel_id,
    roomId: row.room_id,
    botToken: row.bot_token,
    linkedBy: row.linked_by,
    linkedAt: row.linked_at,
  };
}

export function getLink(guildId: string, channelId: string): GuildLink | null {
  const row = db
    .prepare('SELECT * FROM guild_links WHERE guild_id = ? AND channel_id = ?')
    .get(guildId, channelId) as GuildLinkRow | undefined;
  return row ? rowToLink(row) : null;
}

export function saveLink(link: GuildLink): void {
  db.prepare(
    `INSERT INTO guild_links (guild_id, channel_id, room_id, bot_token, linked_by, linked_at)
     VALUES (@guildId, @channelId, @roomId, @botToken, @linkedBy, @linkedAt)
     ON CONFLICT(guild_id, channel_id) DO UPDATE SET
       room_id = excluded.room_id,
       bot_token = excluded.bot_token,
       linked_by = excluded.linked_by,
       linked_at = excluded.linked_at`,
  ).run(link);
}

export function removeLink(guildId: string, channelId: string): boolean {
  const result = db.prepare('DELETE FROM guild_links WHERE guild_id = ? AND channel_id = ?').run(guildId, channelId);
  return result.changes > 0;
}

export function allLinks(): GuildLink[] {
  const rows = db.prepare('SELECT * FROM guild_links').all() as GuildLinkRow[];
  return rows.map(rowToLink);
}

export function rememberSubmission(discordUserId: string, guildId: string, channelId: string, submissionId: string): void {
  db.prepare(
    `INSERT INTO submissions (discord_user_id, guild_id, channel_id, submission_id, submitted_at)
     VALUES (?, ?, ?, ?, ?)
     ON CONFLICT(discord_user_id, guild_id, channel_id) DO UPDATE SET
       submission_id = excluded.submission_id,
       submitted_at = excluded.submitted_at`,
  ).run(discordUserId, guildId, channelId, submissionId, new Date().toISOString());
}

export function lastSubmissionId(discordUserId: string, guildId: string, channelId: string): string | null {
  const row = db
    .prepare('SELECT submission_id FROM submissions WHERE discord_user_id = ? AND guild_id = ? AND channel_id = ?')
    .get(discordUserId, guildId, channelId) as { submission_id: string } | undefined;
  return row?.submission_id ?? null;
}
