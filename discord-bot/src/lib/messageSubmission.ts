import type { Message } from 'discord.js';
import { getLink, rememberSubmission, trackSubmission } from './db.js';
import { submitRequest, uploadFile } from './sqApi.js';
import { fetchOembedMetadata } from './metadata.js';
import { extractRequestLink } from './requestLinks.js';

const AUDIO_EXTENSIONS = ['.mp3', '.wav', '.flac', '.m4a', '.aac', '.ogg', '.opus', '.webm'];
const SPECTRALIS_EXTENSIONS = ['.spectral', '.spectralis'];
const ACCEPTED_EXTENSIONS = [...AUDIO_EXTENSIONS, ...SPECTRALIS_EXTENSIONS];

function extensionOf(filename: string): string {
  const dot = filename.lastIndexOf('.');
  return dot === -1 ? '' : filename.slice(dot).toLowerCase();
}

/** Called on every message in a linked channel. Not every message is a submission
 * attempt — small talk is silently ignored. A recognized link or an audio/.spectral(is)
 * attachment gets submitted to the room and reacted on; the reaction is then kept in
 * sync with the submission's lifecycle by the poller (see nowPlayingPoller.ts). */
export async function handlePotentialSubmission(message: Message): Promise<void> {
  if (message.author.bot || !message.guildId) return;

  const link = getLink(message.guildId, message.channelId);
  if (!link) return;

  const url = extractRequestLink(message.content);
  const attachment = message.attachments.find((a) => ACCEPTED_EXTENSIONS.includes(extensionOf(a.name)));

  if (!url && !attachment) return;

  try {
    let submissionId: string;
    if (attachment) {
      if (attachment.size > 100 * 1024 * 1024) throw new Error('Attachment exceeds 100 MB.');
      const res = await fetch(attachment.url, { signal: AbortSignal.timeout(60_000) });
      if (!res.ok) throw new Error(`couldn't fetch attachment (${res.status})`);
      const bytes = new Uint8Array(await res.arrayBuffer());
      const result = await uploadFile(link.roomId, message.author.id, message.member?.displayName ?? message.author.username, bytes, attachment.name, 'normal', null, message.channelId);
      submissionId = result.submissionId;
    } else {
      const metadata = await fetchOembedMetadata(url!);
      const result = await submitRequest(link.roomId, message.author.id, message.member?.displayName ?? message.author.username, url!, 'normal', metadata, message.channelId);
      submissionId = result.submissionId;
    }

    rememberSubmission(message.author.id, message.guildId, message.channelId, submissionId);
    trackSubmission(submissionId, message.guildId, message.channelId, message.id, 'queued');
    await message.react('📥').catch(() => {});
  } catch {
    await message.react('❌').catch(() => {});
  }
}
