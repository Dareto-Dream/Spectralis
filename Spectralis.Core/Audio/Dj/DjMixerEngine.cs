using System.Threading.Channels;
using NAudio.Wave;
using Spectralis.Core.Platform;
using Spectralis.Core.Visualizers;

namespace Spectralis.Core.Audio.Dj;

public enum DjTransport { PlayPause, Stop, SetCue, Cue, LoopIn, LoopOut, ToggleLoop, HalfLoop, DoubleLoop }

/// <summary>A single audio clock renders both decks and fans out the pre-fader cue bus.
/// The headphone device consumes a bounded copy and never advances deck positions.</summary>
public sealed class DjMixerEngine : IAudioSampleSource, IDisposable
{
    private readonly object _gate = new();
    private readonly DjDeck[] _decks = [new(), new()];
    private readonly IAudioDeviceEnumerator _devices;
    private IAudioDevice? _masterDevice, _cueDevice;
    private readonly CueBuffer _cue = new();
    private readonly float[] _a = new float[4096], _b = new float[4096], _cueBlock = new float[4096];
    private MixRecorder? _recorder;
    private bool _disposed;
    private VisualizerSampleProvider? _visualizer;
    private long _renderedFrames;
    public double RenderedSeconds { get { lock (_gate) return _renderedFrames / (double)SampleRate; } }
    public VisualizerFrame GetVisualizerFrame(bool includeSpectrogram = false, bool includeRawFft = false)
    {
        lock (_gate)
        {
            if (_disposed) return VisualizerFrame.Empty;
            _visualizer ??= new VisualizerSampleProvider(new NAudio.Wave.SilenceProvider(
                WaveFormat.CreateIeeeFloatWaveFormat(SampleRate, Channels)).ToSampleProvider());
            return _visualizer.GetFrame(includeSpectrogram, includeRawFft);
        }
    }
    private double _crossfader, _masterVolume = .8, _cueVolume = .7;
    public event EventHandler<string>? OutputFailed;
    public int SampleRate => DjTrack.SampleRate;
    public int Channels => 2;
    public float MasterPeak { get; private set; }
    public bool Clipping { get; private set; }
    public bool IsRecording { get { lock (_gate) return _recorder is not null; } }
    public string? RecordingError { get { lock (_gate) return _recorder?.Error; } }
    public double Crossfader { get { lock (_gate) return _crossfader; } set { lock (_gate) _crossfader = double.IsFinite(value) ? Math.Clamp(value, -1, 1) : 0; } }
    public double MasterVolume { get { lock (_gate) return _masterVolume; } set { lock (_gate) _masterVolume = double.IsFinite(value) ? Math.Clamp(value, 0, 1) : 0; } }
    public double CueVolume { get { lock (_gate) return _cueVolume; } set { lock (_gate) _cueVolume = double.IsFinite(value) ? Math.Clamp(value, 0, 1) : 0; } }
    public DjMixerEngine(IAudioDeviceEnumerator? devices = null) => _devices = devices ?? AudioDeviceEnumeratorFactory.Create();
    public IReadOnlyList<AudioDeviceInfo> GetDevices() => _devices.GetOutputDevices();
    public DjDeckState GetDeck(int deck) { lock (_gate) return _decks[deck].Snapshot(); }
    public void ConfigureDeck(int deck, DjDeckSettings settings) { lock (_gate) _decks[deck].Configure(settings); }
    public void LoadDeck(int deck, DjTrack track)
    {
        lock (_gate) { ObjectDisposedException.ThrowIf(_disposed, this); _decks[deck].Load(track); _cue.Clear(); }
    }
    public void RestoreCues(int deck, double cue, double?[] hotCues) { lock (_gate) _decks[deck].RestoreCues(cue, hotCues); }
    public void Seek(int deck, double seconds) { lock (_gate) { _decks[deck].Seek(seconds); _cue.Clear(); } }
    public void HotCue(int deck, int slot, bool clear = false) { lock (_gate) { _decks[deck].HotCue(slot, clear); _cue.Clear(); } }
    public void BeatJump(int deck, int beats) { lock (_gate) { _decks[deck].BeatJump(beats); _cue.Clear(); } }
    public void BeatLoop(int deck, double beats) { lock (_gate) { _decks[deck].BeatLoop(beats); _cue.Clear(); } }
    public void Nudge(int deck, double amount) { lock (_gate) _decks[deck].Nudge(amount); }
    public void SetStem(int deck, int stem, double volume, bool muted, bool solo) { lock (_gate) _decks[deck].SetStem(stem, volume, muted, solo); }
    public void Transport(int deck, DjTransport action)
    {
        lock (_gate)
        {
            var d = _decks[deck];
            switch (action)
            {
                case DjTransport.PlayPause: d.Toggle(); break;
                case DjTransport.Stop: d.Stop(); break;
                case DjTransport.SetCue: d.SetCue(); break;
                case DjTransport.Cue: d.ReturnToCue(); break;
                case DjTransport.LoopIn: d.SetLoopIn(); break;
                case DjTransport.LoopOut: d.SetLoopOut(); break;
                case DjTransport.ToggleLoop: d.ToggleLoop(); break;
                case DjTransport.HalfLoop: d.ResizeLoop(.5); break;
                case DjTransport.DoubleLoop: d.ResizeLoop(2); break;
            }
        }
    }

    public void Sync(int target)
    {
        lock (_gate)
        {
            var leader = _decks[1 - target];
            var follower = _decks[target];
            if (leader.Duration == 0 || follower.Duration == 0) throw new InvalidOperationException("Load both decks before syncing.");
            var tempo = (leader.EffectiveBpm / follower.Settings.Bpm - 1) * 100;
            if (tempo is < -50 or > 50) throw new InvalidOperationException("BPMs are too far apart. Use half/double BPM or adjust the grid first.");
            follower.Configure(follower.Settings with { TempoPercent = tempo });
            var phase = (leader.Position - leader.Settings.BeatOffset) * leader.Settings.Bpm / 60;
            phase -= Math.Floor(phase);
            var beat = Math.Floor((follower.Position - follower.Settings.BeatOffset) * follower.Settings.Bpm / 60);
            follower.Seek(follower.Settings.BeatOffset + (beat + phase) * 60 / follower.Settings.Bpm);
        }
    }

    public void StartOutput(string? masterId, string? cueId = null)
    {
        StopOutput();
        if (cueId is not null)
        {
            var defaultId = GetDevices().FirstOrDefault(d => d.IsDefault)?.Id;
            if (cueId == (masterId ?? defaultId)) throw new InvalidOperationException("Choose a different device for headphones and master output.");
        }
        try
        {
            _masterDevice = _devices.CreateDevice(masterId, 50);
            _masterDevice.Volume = 1;
            _masterDevice.Init(this);
            _masterDevice.PlaybackStopped += OnOutputStopped;
            if (cueId is not null)
            {
                _cueDevice = _devices.CreateDevice(cueId, 50);
                _cueDevice.Volume = 1;
                _cueDevice.Init(_cue);
                _cueDevice.PlaybackStopped += OnOutputStopped;
                _cueDevice.Play();
            }
            _masterDevice.Play();
        }
        catch { StopOutput(); throw; }
    }

    private void OnOutputStopped(object? sender, AudioDeviceStoppedEventArgs args)
    {
        if (args.Exception is null) return;
        PauseAll();
        OutputFailed?.Invoke(this, args.Exception.Message);
    }
    public void PauseAll() { lock (_gate) { foreach (var deck in _decks) deck.Pause(); _cue.Clear(); } }
    public void UnloadAll()
    {
        lock (_gate)
        {
            for (var i = 0; i < _decks.Length; i++) { _decks[i].Dispose(); _decks[i] = new DjDeck(); }
            _cue.Clear();
            _visualizer?.Clear();
        }
    }
    public void StopOutput()
    {
        // Dispose devices outside the render lock: output disposal can wait for an in-flight callback.
        if (_masterDevice is { } master) { _masterDevice = null; master.PlaybackStopped -= OnOutputStopped; master.Dispose(); }
        if (_cueDevice is { } cue) { _cueDevice = null; cue.PlaybackStopped -= OnOutputStopped; cue.Dispose(); }
        _cue.Clear();
    }
    public void StartRecording(string path)
    {
        lock (_gate)
        {
            if (_recorder is not null) throw new InvalidOperationException("A recording is already running.");
            _recorder = new MixRecorder(path);
        }
    }
    public async Task StopRecordingAsync()
    {
        MixRecorder? recorder;
        lock (_gate) { recorder = _recorder; _recorder = null; }
        if (recorder is not null) await recorder.FinishAsync().ConfigureAwait(false);
    }

    public int Read(float[] buffer, int offset, int count)
    {
        lock (_gate)
        {
            Array.Clear(buffer, offset, count);
            if (_disposed) return 0;
            var gainA = Math.Cos((_crossfader + 1) * Math.PI / 4) * _decks[0].Settings.Fader;
            var gainB = Math.Sin((_crossfader + 1) * Math.PI / 4) * _decks[1].Settings.Fader;
            float peak = 0;
            Clipping = false;
            for (var position = 0; position < count;)
            {
                var n = Math.Min(_a.Length, count - position);
                n -= n % 2;
                if (n == 0) break;
                _decks[0].Read(_a, 0, n); _decks[1].Read(_b, 0, n);
                for (var i = 0; i < n; i++)
                {
                    var mix = (_a[i] * gainA + _b[i] * gainB) * _masterVolume;
                    Clipping |= Math.Abs(mix) > 1;
                    var limited = (float)Math.Clamp(mix, -1, 1);
                    buffer[offset + position + i] = limited;
                    peak = Math.Max(peak, Math.Abs(limited));
                    _cueBlock[i] = (float)Math.Clamp(((_decks[0].Settings.HeadphoneCue ? _a[i] : 0) +
                        (_decks[1].Settings.HeadphoneCue ? _b[i] : 0)) * _cueVolume, -1, 1);
                }
                if (_cueDevice is not null) _cue.Write(_cueBlock, n);
                position += n;
            }
            MasterPeak = peak;
            // Analyze precisely the limited master mix the output device hears.
            // Visualizer windows only read snapshots; no second playback clock.
            _visualizer?.FeedExternalSamples(buffer, offset, count, Channels);
            _renderedFrames += count / Channels;
            _recorder?.Write(buffer, offset, count);
            return count;
        }
    }

    public void Dispose()
    {
        StopOutput();
        try { StopRecordingAsync().GetAwaiter().GetResult(); }
        finally { lock (_gate) { _disposed = true; foreach (var deck in _decks) deck.Dispose(); } }
    }

    private sealed class CueBuffer : IAudioSampleSource
    {
        private readonly object _sync = new();
        private readonly float[] _samples = new float[DjTrack.SampleRate / 2];
        private int _read, _write, _count;
        public int SampleRate => DjTrack.SampleRate;
        public int Channels => 2;
        public void Clear() { lock (_sync) _read = _write = _count = 0; }
        public void Write(float[] samples, int count)
        {
            lock (_sync)
            {
                for (var i = 0; i < count; i++)
                {
                    if (_count == _samples.Length) { _read = (_read + 1) % _samples.Length; _count--; }
                    _samples[_write] = samples[i]; _write = (_write + 1) % _samples.Length; _count++;
                }
            }
        }
        public int Read(float[] buffer, int offset, int count)
        {
            lock (_sync)
            {
                Array.Clear(buffer, offset, count);
                var n = Math.Min(count, _count);
                for (var i = 0; i < n; i++) { buffer[offset + i] = _samples[_read]; _read = (_read + 1) % _samples.Length; }
                _count -= n;
                return count;
            }
        }
    }

    private sealed class MixRecorder
    {
        private readonly Channel<float[]> _blocks = Channel.CreateBounded<float[]>(128);
        private readonly Task _writerTask;
        public string? Error { get; private set; }
        public MixRecorder(string path)
        {
            // CreateNew prevents silently replacing an existing mix.
            var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
            var writer = new WaveFileWriter(file, WaveFormat.CreateIeeeFloatWaveFormat(DjTrack.SampleRate, 2));
            _writerTask = Task.Run(async () =>
            {
                using (writer)
                {
                    try
                    {
                        await foreach (var block in _blocks.Reader.ReadAllAsync().ConfigureAwait(false))
                        {
                            if (writer.Length > uint.MaxValue - 65536) throw new IOException("WAV size limit reached. Start a new recording.");
                            writer.WriteSamples(block, 0, block.Length);
                        }
                    }
                    catch (Exception ex) { Error = ex.Message; _blocks.Writer.TryComplete(); }
                }
            });
        }
        public void Write(float[] buffer, int offset, int count)
        {
            if (Error is not null) return;
            if (!_blocks.Writer.TryWrite(buffer.AsSpan(offset, count).ToArray()))
            {
                Error = "Recording stopped: the disk could not keep up with the mix.";
                _blocks.Writer.TryComplete();
            }
        }
        public async Task FinishAsync()
        {
            _blocks.Writer.TryComplete();
            await _writerTask.ConfigureAwait(false);
            if (Error is not null) throw new IOException(Error);
        }
    }
}
