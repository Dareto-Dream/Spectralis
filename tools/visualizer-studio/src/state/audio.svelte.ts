import { sha256Hex } from '../export/sha256';

export interface AudioLevel {
  peak: number;
  rms: number;
}

function computePeaks(buffer: AudioBuffer, resolution: number): Float32Array {
  const channel = buffer.getChannelData(0);
  const peaks = new Float32Array(resolution);
  const bucketSize = Math.max(1, Math.floor(channel.length / resolution));
  for (let i = 0; i < resolution; i++) {
    let max = 0;
    const start = i * bucketSize;
    const end = Math.min(start + bucketSize, channel.length);
    for (let j = start; j < end; j++) {
      const v = Math.abs(channel[j]);
      if (v > max) max = v;
    }
    peaks[i] = max;
  }
  return peaks;
}

// Web Audio's MediaElementSource can only be created ONCE per <audio> element —
// the analyser graph is wired up lazily on the first load and reused for every
// subsequent file, rather than rebuilt (which would throw on the second load).
export class AudioState {
  el: HTMLAudioElement = new Audio();
  private ctx: AudioContext | null = null;
  private analyser: AnalyserNode | null = null;
  private objectUrl: string | null = null;

  file: File | null = $state(null);
  loaded = $state(false);
  error: string | null = $state(null);
  waveformPeaks: Float32Array | null = $state(null);
  sha256: string | null = $state(null);

  private ensureGraph() {
    if (this.ctx) return;
    this.ctx = new AudioContext();
    this.analyser = this.ctx.createAnalyser();
    this.analyser.fftSize = 1024;
    const source = this.ctx.createMediaElementSource(this.el);
    source.connect(this.analyser);
    this.analyser.connect(this.ctx.destination);
  }

  async loadFile(file: File) {
    this.error = null;
    this.waveformPeaks = null;
    this.sha256 = null;
    if (this.objectUrl) URL.revokeObjectURL(this.objectUrl);
    this.objectUrl = URL.createObjectURL(file);
    this.el.src = this.objectUrl;
    this.file = file;
    try {
      this.ensureGraph();
      if (this.ctx?.state === 'suspended') await this.ctx.resume();
      this.loaded = true;
    } catch (err) {
      // QoL: surface decode/setup failure instead of only console-warning, so the
      // waveform lane and export gate can correctly fall back to their no-audio
      // empty states instead of half-loading.
      this.loaded = false;
      this.error = err instanceof Error ? err.message : 'Could not load this audio file.';
      return;
    }
    // One read, two independent consumers: decodeAudioData detaches whatever
    // buffer it's given, so the waveform decode gets a .slice(0) copy while the
    // original `raw` stays intact for hashing (Gap 1 — real audio sha256).
    const raw = await file.arrayBuffer();
    try {
      const decoded = await this.ctx!.decodeAudioData(raw.slice(0));
      this.waveformPeaks = computePeaks(decoded, 2000);
    } catch {
      // Waveform is a nice-to-have on top of already-working playback — a decode
      // failure here just means the timeline falls back to its "no waveform" state,
      // not a hard error for the whole load.
      this.waveformPeaks = null;
    }
    try {
      this.sha256 = await sha256Hex(raw);
    } catch {
      this.sha256 = null;
    }
  }

  // Mirrors AssetsState.extension — 'wav' fallback matches the export
  // pipeline's old hardcoded literal for the not-yet-loaded case.
  get extension(): string {
    if (!this.file) return 'wav';
    const name = this.file.name;
    const dot = name.lastIndexOf('.');
    return dot >= 0 ? name.slice(dot + 1).toLowerCase() : 'wav';
  }

  currentLevel(): AudioLevel {
    if (!this.analyser) return { peak: 0, rms: 0 };
    const buf = new Uint8Array(this.analyser.frequencyBinCount);
    this.analyser.getByteTimeDomainData(buf);
    let peak = 0;
    let sumSq = 0;
    for (let i = 0; i < buf.length; i++) {
      const v = (buf[i] - 128) / 128;
      peak = Math.max(peak, Math.abs(v));
      sumSq += v * v;
    }
    return { peak, rms: Math.sqrt(sumSq / buf.length) };
  }

  dispose() {
    if (this.objectUrl) URL.revokeObjectURL(this.objectUrl);
  }
}
