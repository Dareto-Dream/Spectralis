// Backs the Script Console docker (Tools > Script Console). Runs plain
// browser JavaScript against a small, curated API object per target
// workspace — NOT literal Node.js: no `require`, no filesystem, no network.
// This ships as a static webpage with no backend, so "small scripts" here
// means "batch-edit the current project through its store API", the same
// category of power as a DAW's scripting console, not a real runtime.
//
// `new Function` gives isolated variable scope for the injected API/console
// names, not a security sandbox — the script still runs with the page's full
// privileges. That's an acceptable trust model for a local creative tool
// where the only person who can type into this console is the person running
// the app, same as a browser's own devtools console.
export interface ScriptResult {
  ok: boolean;
  logs: string[];
  error?: string;
}

function stringifyArg(a: unknown): string {
  if (typeof a === 'string') return a;
  if (a instanceof Error) return `${a.name}: ${a.message}`;
  try {
    return JSON.stringify(a, null, 2);
  } catch {
    return String(a);
  }
}

export function runScript(code: string, ctx: Record<string, unknown>): ScriptResult {
  const logs: string[] = [];
  const sandboxConsole = {
    log: (...args: unknown[]) => logs.push(args.map(stringifyArg).join(' ')),
    info: (...args: unknown[]) => logs.push(args.map(stringifyArg).join(' ')),
    warn: (...args: unknown[]) => logs.push('⚠ ' + args.map(stringifyArg).join(' ')),
    error: (...args: unknown[]) => logs.push('✗ ' + args.map(stringifyArg).join(' ')),
  };
  try {
    const argNames = ['console', ...Object.keys(ctx)];
    const argValues = [sandboxConsole, ...Object.values(ctx)];
    // eslint-disable-next-line @typescript-eslint/no-implied-eval
    const fn = new Function(...argNames, code);
    fn(...argValues);
    return { ok: true, logs };
  } catch (err) {
    return { ok: false, logs, error: err instanceof Error ? `${err.name}: ${err.message}` : String(err) };
  }
}
