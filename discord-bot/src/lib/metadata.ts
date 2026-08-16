/** Best-effort title/artist lookup for a submitted URL via noembed.com — same free
 * oEmbed proxy sq.js uses client-side (see tryAutofillFromUrl/fetchOembed in
 * web-share/sq.js) so Discord submissions show accurate metadata in the queue instead
 * of falling back to the submitter's display name. Never throws — a lookup failure
 * just means the queue entry keeps whatever title/artist it already had. */
export async function fetchOembedMetadata(url: string): Promise<{ title: string | null; artist: string | null }> {
  try {
    const res = await fetch(`https://noembed.com/embed?url=${encodeURIComponent(url)}`, {
      signal: AbortSignal.timeout(5000),
    });
    if (!res.ok) return { title: null, artist: null };
    const data = (await res.json()) as { title?: string; author_name?: string };
    return { title: data.title ?? null, artist: data.author_name ?? null };
  } catch {
    return { title: null, artist: null };
  }
}
