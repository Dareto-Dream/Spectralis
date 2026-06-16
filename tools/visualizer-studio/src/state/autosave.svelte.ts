const KEY = 'visualizer-studio:autosave';
const DEBOUNCE_MS = 500;

export interface AutosaveEntry {
  project: unknown;
  savedAt: number;
}

// A safety net on top of the file-based Save/Load flow, not a replacement for it —
// cleared on a successful Save Project. localStorage can throw (quota, private
// browsing), so every access is wrapped: autosave failing must never be fatal.
export class AutosaveManager {
  private timer: ReturnType<typeof setTimeout> | null = null;
  private lastWriteAt = 0;

  scheduleSave(project: unknown) {
    if (this.timer) clearTimeout(this.timer);
    this.timer = setTimeout(() => this.saveNow(project), DEBOUNCE_MS);
  }

  saveNow(project: unknown) {
    try {
      const entry: AutosaveEntry = { project, savedAt: Date.now() };
      localStorage.setItem(KEY, JSON.stringify(entry));
      this.lastWriteAt = Date.now();
    } catch {
      // best-effort — see class comment
    }
    this.timer = null;
  }

  isStale(thresholdMs = 2000): boolean {
    return Date.now() - this.lastWriteAt > thresholdMs;
  }

  readSaved(): AutosaveEntry | null {
    try {
      const raw = localStorage.getItem(KEY);
      return raw ? (JSON.parse(raw) as AutosaveEntry) : null;
    } catch {
      return null;
    }
  }

  clear() {
    try {
      localStorage.removeItem(KEY);
    } catch {
      // best-effort — see class comment
    }
  }
}

export const autosave = new AutosaveManager();
