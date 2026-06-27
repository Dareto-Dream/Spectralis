// File > Settings — theme + typography customization. Every preset is built
// from the SAME token set app.css already defines (--bg0..4/--line/--text/
// --dim/--accent/...) rather than a separate one-off palette per preset, and
// OLED stays true #000 per the app's existing visual-language rule — this is
// palette swaps, not a new "AI dashboard" skin.
export interface ThemeTokens {
  bg0: string; bg1: string; bg2: string; bg3: string; bg4: string;
  line: string; line2: string;
  text: string; dim: string; dim2: string;
  accent: string; accent2: string; danger: string; good: string;
}

export interface ThemePreset {
  id: string;
  label: string;
  tokens: ThemeTokens;
}

export const THEME_PRESETS: ThemePreset[] = [
  {
    id: 'void',
    label: 'Void (default)',
    tokens: {
      bg0: '#0a0a0d', bg1: '#111116', bg2: '#17171e', bg3: '#1e1e27', bg4: '#26262f',
      line: '#2c2c37', line2: '#3a3a47',
      text: '#e7e7ee', dim: '#9494a6', dim2: '#6a6a7a',
      accent: '#7fb7ff', accent2: '#ffd06e', danger: '#ff6e6e', good: '#7fe0a8',
    },
  },
  {
    id: 'oled',
    label: 'OLED (true black)',
    tokens: {
      bg0: '#000000', bg1: '#0a0a0a', bg2: '#121212', bg3: '#1a1a1a', bg4: '#222222',
      line: '#2a2a2a', line2: '#383838',
      text: '#f0f0f0', dim: '#a0a0a0', dim2: '#707070',
      accent: '#7fb7ff', accent2: '#ffd06e', danger: '#ff6e6e', good: '#7fe0a8',
    },
  },
  {
    id: 'slate',
    label: 'Slate',
    tokens: {
      bg0: '#0b0e14', bg1: '#121620', bg2: '#181d2a', bg3: '#202635', bg4: '#29303f',
      line: '#313850', line2: '#3f4864',
      text: '#e6e9f2', dim: '#8f97ac', dim2: '#626a80',
      accent: '#6fa8ff', accent2: '#ffcf6e', danger: '#ff6e6e', good: '#7fe0a8',
    },
  },
  {
    id: 'amber',
    label: 'Amber Terminal',
    tokens: {
      bg0: '#0d0a06', bg1: '#15100a', bg2: '#1c150d', bg3: '#241b10', bg4: '#2c2214',
      line: '#382a19', line2: '#4a3a22',
      text: '#f2e6d0', dim: '#b0a080', dim2: '#7a6f56',
      accent: '#ffb454', accent2: '#6ee7ff', danger: '#ff6e6e', good: '#7fe0a8',
    },
  },
];

export interface FontOption {
  id: string;
  label: string;
  stack: string;
}

// Local-safe stacks only — this app has to keep working standalone via
// file:// with no network, so no @import of a web font. Each entry degrades
// gracefully through its fallback chain if the named face isn't installed.
export const FONT_OPTIONS: FontOption[] = [
  { id: 'cascadia', label: 'Cascadia Code (default)', stack: '"Cascadia Code", "Consolas", monospace' },
  { id: 'consolas', label: 'Consolas', stack: '"Consolas", "Courier New", monospace' },
  { id: 'jetbrains', label: 'JetBrains Mono', stack: '"JetBrains Mono", "Cascadia Code", monospace' },
  { id: 'system', label: 'System UI Mono', stack: 'ui-monospace, "SFMono-Regular", "Consolas", monospace' },
  { id: 'menlo', label: 'Menlo / Monaco', stack: '"Menlo", "Monaco", "Consolas", monospace' },
];

const STORAGE_KEY = 'visualizer-studio:theme';
const MIN_SCALE = 0.85;
const MAX_SCALE = 1.3;

interface StoredTheme {
  presetId: string;
  customAccent: string | null;
  fontId: string;
  uiScale: number;
}

function loadStored(): StoredTheme | null {
  try {
    const raw = localStorage.getItem(STORAGE_KEY);
    return raw ? (JSON.parse(raw) as StoredTheme) : null;
  } catch {
    return null;
  }
}

export class ThemeSettings {
  presetId = $state('void');
  customAccent: string | null = $state(null);
  fontId = $state('cascadia');
  uiScale = $state(1);

  constructor() {
    const stored = loadStored();
    if (stored) {
      this.presetId = stored.presetId ?? 'void';
      this.customAccent = stored.customAccent ?? null;
      this.fontId = stored.fontId ?? 'cascadia';
      this.uiScale = stored.uiScale ?? 1;
    }
  }

  get preset(): ThemePreset {
    return THEME_PRESETS.find((p) => p.id === this.presetId) ?? THEME_PRESETS[0];
  }

  get font(): FontOption {
    return FONT_OPTIONS.find((f) => f.id === this.fontId) ?? FONT_OPTIONS[0];
  }

  setPreset(id: string) {
    this.presetId = id;
    this.persistAndApply();
  }

  setCustomAccent(hex: string | null) {
    this.customAccent = hex;
    this.persistAndApply();
  }

  setFont(id: string) {
    this.fontId = id;
    this.persistAndApply();
  }

  setUiScale(scale: number) {
    this.uiScale = Math.max(MIN_SCALE, Math.min(MAX_SCALE, scale));
    this.persistAndApply();
  }

  resetToDefaults() {
    this.presetId = 'void';
    this.customAccent = null;
    this.fontId = 'cascadia';
    this.uiScale = 1;
    this.persistAndApply();
  }

  private persistAndApply() {
    try {
      const stored: StoredTheme = { presetId: this.presetId, customAccent: this.customAccent, fontId: this.fontId, uiScale: this.uiScale };
      localStorage.setItem(STORAGE_KEY, JSON.stringify(stored));
    } catch {
      // best-effort — theme persistence is a convenience, never fatal
    }
    this.apply();
  }

  // Sets CSS custom properties on :root — every component already reads
  // --bg0..4/--accent/--mono etc. from there, so this is the only DOM touch
  // point needed; no component-level rewiring.
  apply() {
    const root = document.documentElement.style;
    const t = this.preset.tokens;
    root.setProperty('--bg0', t.bg0);
    root.setProperty('--bg1', t.bg1);
    root.setProperty('--bg2', t.bg2);
    root.setProperty('--bg3', t.bg3);
    root.setProperty('--bg4', t.bg4);
    root.setProperty('--line', t.line);
    root.setProperty('--line2', t.line2);
    root.setProperty('--text', t.text);
    root.setProperty('--dim', t.dim);
    root.setProperty('--dim2', t.dim2);
    root.setProperty('--accent', this.customAccent || t.accent);
    root.setProperty('--accent2', t.accent2);
    root.setProperty('--danger', t.danger);
    root.setProperty('--good', t.good);
    root.setProperty('--mono', this.font.stack);
    root.setProperty('--ui-scale', String(this.uiScale));
  }
}

export const themeSettings = new ThemeSettings();
