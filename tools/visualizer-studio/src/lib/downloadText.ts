export function downloadText(filename: string, text: string) {
  const blob = new Blob([text], { type: 'text/plain' });
  const a = document.createElement('a');
  a.href = URL.createObjectURL(blob);
  a.download = filename;
  document.body.appendChild(a);
  a.click();
  document.body.removeChild(a);
  setTimeout(() => URL.revokeObjectURL(a.href), 4000);
}

// Browser-build fallback for saving a .spectralis/.spectral file — the same
// bytes the native build writes via fs, just delivered as a download since
// there's no filesystem to write to directly.
export function downloadBytes(filename: string, bytes: Uint8Array, mime = 'application/octet-stream') {
  // TS's DOM lib types Uint8Array as generic over ArrayBufferLike (which
  // includes SharedArrayBuffer), but BlobPart only accepts a view backed by a
  // real ArrayBuffer — our bytes are always freshly allocated by
  // src/format/riff.ts, never shared, so this is a type-system gap, not a
  // real runtime concern.
  const blob = new Blob([bytes as Uint8Array<ArrayBuffer>], { type: mime });
  const a = document.createElement('a');
  a.href = URL.createObjectURL(blob);
  a.download = filename;
  document.body.appendChild(a);
  a.click();
  document.body.removeChild(a);
  setTimeout(() => URL.revokeObjectURL(a.href), 4000);
}

// For binary assets (cover images) that already live in memory as a data:
// URL — no Blob needed, the data: URL is already a valid <a href>.
export function downloadDataUrl(filename: string, dataUrl: string) {
  const a = document.createElement('a');
  a.href = dataUrl;
  a.download = filename;
  document.body.appendChild(a);
  a.click();
  document.body.removeChild(a);
}

export interface DeliverOptions {
  dirName: string;
  // Browser fallback needs the actual bytes to download; native only needs a
  // real fs path so the main process can fs.copyFile it directly.
  coverDataUrl?: string | null;
  coverSourcePath?: string | null;
  coverDestName?: string | null;
  // No browser equivalent exists for audio — it's always been BYOF for the
  // pack script there. Native can fs.copyFile the real source file.
  audioSourcePath?: string | null;
  audioDestName?: string | null;
}

// The one thing both runtimes funnel export delivery through. Native: one
// native save-folder dialog, then everything (text files + cover + audio) is
// written by the main process via real fs calls — nothing round-trips
// through renderer memory as a Blob. Browser: falls back to today's behavior,
// one <a download> click per file.
export async function deliverFiles(
  files: { name: string; content: string }[],
  opts: DeliverOptions
): Promise<{ delivered: boolean; dir?: string }> {
  if (window.native) {
    const dir = await window.native.chooseExportDir(opts.dirName);
    if (!dir) return { delivered: false }; // user cancelled the folder picker
    await window.native.writeExport({
      dir,
      files,
      coverSourcePath: opts.coverSourcePath ?? null,
      coverDestName: opts.coverDestName ?? null,
      audioSourcePath: opts.audioSourcePath ?? null,
      audioDestName: opts.audioDestName ?? null,
    });
    return { delivered: true, dir };
  }
  for (const f of files) downloadText(f.name, f.content);
  if (opts.coverDataUrl && opts.coverDestName) downloadDataUrl(opts.coverDestName, opts.coverDataUrl);
  return { delivered: true };
}
