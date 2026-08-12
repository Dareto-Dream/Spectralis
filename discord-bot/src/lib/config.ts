import 'dotenv/config';

function required(name: string): string {
  const value = process.env[name];
  if (!value) throw new Error(`Missing required env var ${name} (see .env.example).`);
  return value;
}

export const config = {
  discordToken: required('DISCORD_TOKEN'),
  discordClientId: required('DISCORD_CLIENT_ID'),
  sqApiBaseUrl: (process.env.SQ_API_BASE_URL ?? 'https://audioplayer-production-5b83.up.railway.app').replace(/\/+$/, ''),
  dbPath: process.env.DB_PATH ?? './data/bot.sqlite3',
};
