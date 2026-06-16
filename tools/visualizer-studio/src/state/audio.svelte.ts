export interface AudioLevel {
  peak: number;
  rms: number;
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
    }
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
