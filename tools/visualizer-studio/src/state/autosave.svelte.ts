// A safety net on top of the file-based Save/Load flow, not a replacement for
// it — cleared on a successful Save. Now format-aware: this used to be one
// global localStorage key holding raw `Project` JSON, which never touched the
// real .spectralis/.spectral binary encoding and had no World-mode equivalent
// at all. Generic over T (a CapsuleFile or a WorldFile) so both modes share
// one implementation and exercise the exact same encode/decode path manual
// Save/Load already uses — no second "JSON shape" to keep in sync.
//
// Native: writes a real rotating file into Documents/<studio>/Autosaves/ via
// the same writeBinaryFile/getStudioRoot IPC manual save uses. Browser: no
// real filesystem to autosave into, so it stays in localStorage — but now
// storing the actual encoded binary (base64) instead of plain JSON, per the
// "web gets a lighter capability, backed by browser cache" requirement.
import { encodeCapsuleFile, decodeCapsuleFile, type CapsuleFile } from '../format/capsuleFile';
import { encodeWorldFile, decodeWorldFile, type WorldFile } from '../format/worldFile';

const DEBOUNCE_MS = 500;

function bytesToBase64(bytes: Uint8Array): string {
  let binary = '';
  for (let i = 0; i < bytes.length; i++) binary += String.fromCharCode(bytes[i]);
  return btoa(binary);
}
function base64ToBytes(b64: string): Uint8Array {
  const binary = atob(b64);
  const bytes = new Uint8Array(binary.length);
  for (let i = 0; i < binary.length; i++) bytes[i] = binary.charCodeAt(i);
  return bytes;
}

export class AutosaveManager<T> {
  private timer: ReturnType<typeof setTimeout> | null = null;
  private lastWriteAt = 0;

  constructor(
    private nativeFileName: string,
    private storageKey: string,
    private encode: (data: T) => Uint8Array,
    private decode: (bytes: Uint8Array) => T
  ) {}

  scheduleSave(data: T) {
    if (this.timer) clearTimeout(this.timer);
    this.timer = setTimeout(() => void this.saveNow(data), DEBOUNCE_MS);
  }

  async saveNow(data: T) {
    this.timer = null;
    try {
      const bytes = this.encode(data);
      if (window.native) {
        const root = await window.native.getStudioRoot();
        // Same ArrayBuffer-vs-ArrayBufferLike gap as every other native write
        // path in this codebase — `bytes` is always a fresh, non-shared buffer.
        await window.native.writeBinaryFile(
          `${root}/Autosaves/${this.nativeFileName}`,
          bytes.buffer.slice(bytes.byteOffset, bytes.byteOffset + bytes.byteLength) as ArrayBuffer
        );
      } else {
        localStorage.setItem(this.storageKey, bytesToBase64(bytes));
      }
      this.lastWriteAt = Date.now();
    } catch {
      // best-effort — autosave failing must never be fatal
    }
  }

  isStale(thresholdMs = 2000): boolean {
    return Date.now() - this.lastWriteAt > thresholdMs;
  }

  async readSaved(): Promise<T | null> {
    try {
      if (window.native) {
        const root = await window.native.getStudioRoot();
        const buf = await window.native.readBinaryFile(`${root}/Autosaves/${this.nativeFileName}`);
        return this.decode(new Uint8Array(buf));
      }
      const raw = localStorage.getItem(this.storageKey);
      return raw ? this.decode(base64ToBytes(raw)) : null;
    } catch {
      // Missing file (nothing autosaved yet) and a corrupted entry both land
      // here — either way, "nothing to restore" is the right answer.
      return null;
    }
  }

  async clear() {
    try {
      if (window.native) {
        const root = await window.native.getStudioRoot();
        await window.native.deleteFile(`${root}/Autosaves/${this.nativeFileName}`);
      } else {
        localStorage.removeItem(this.storageKey);
      }
    } catch {
      // best-effort — see saveNow
    }
  }
}

export const capsuleAutosave = new AutosaveManager<CapsuleFile>(
  'capsule-autosave.spectralis',
  'visualizer-studio:autosave:capsule',
  (d) => encodeCapsuleFile(d),
  decodeCapsuleFile
);

export const worldAutosave = new AutosaveManager<WorldFile>(
  'world-autosave.spectral',
  'visualizer-studio:autosave:world',
  (d) => encodeWorldFile(d),
  decodeWorldFile
);
