using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using Spectralis.Core.Audio.Effects;

namespace Spectralis.Core.Audio.Dj;

public sealed record DjDeckSettings(
    double TempoPercent = 0, bool KeyLock = false, double Bpm = 120, double BeatOffset = 0,
    double GainDb = 0, double Fader = 1, double LowDb = 0, double MidDb = 0, double HighDb = 0,
    double Filter = 0, bool HeadphoneCue = false, bool Quantize = true);
public sealed record DjStemState(string Name, double Volume, bool Muted, bool Solo);
public sealed record DjDeckState(string? Path, bool Playing, double Position, double Duration,
    double Cue, double? LoopIn, double? LoopOut, bool LoopEnabled, double?[] HotCues,
    DjDeckSettings Settings, string Key, float[] Waveform, DjStemState[] Stems, float Peak);

/// <summary>Deck DSP and transport. All access is serialized by DjMixerEngine's shared lock.</summary>
internal sealed class DjDeck : ISampleProvider, IDisposable
{
    private DjTrack? _track;
    private double _frame;
    private ISampleProvider _pitch;
    private readonly Biquad[,] _eq = new Biquad[2, 4];
    private DjStemState[] _stems = [];
    private double _envelope;
    private double _nudge = 1;
    private int _nudgeFrames;
    private double _cue;
    private double? _loopIn, _loopOut;
    private readonly double?[] _hotCues = new double?[4];
    public bool Playing { get; private set; }
    public bool LoopEnabled { get; private set; }
    public DjDeckSettings Settings { get; private set; } = new();
    public WaveFormat WaveFormat { get; } = WaveFormat.CreateIeeeFloatWaveFormat(DjTrack.SampleRate, 2);
    public float Peak { get; private set; }
    public double Duration => _track?.Duration ?? 0;
    public double Position => _frame / DjTrack.SampleRate;
    public double EffectiveBpm => Settings.Bpm * Rate;
    private double Rate => 1 + Settings.TempoPercent / 100;

    public DjDeck()
    {
        for (var ch = 0; ch < 2; ch++) for (var band = 0; band < 4; band++) _eq[ch, band] = new Biquad();
        _pitch = new DeckSource(this);
        Configure(Settings);
    }

    public DjDeckState Snapshot() => new(_track?.Path, Playing, Position, Duration, _cue, _loopIn, _loopOut,
        LoopEnabled, (double?[])_hotCues.Clone(), Settings, _track?.Key ?? "", _track?.Waveform ?? [], (DjStemState[])_stems.Clone(), Peak);

    public void Load(DjTrack track)
    {
        if (Playing) throw new InvalidOperationException("Pause this deck before replacing its track.");
        _track?.Dispose();
        _track = track;
        _frame = 0; _cue = 0; _loopIn = _loopOut = null; LoopEnabled = false;
        Array.Clear(_hotCues);
        _stems = track.Stems.Select(s => new DjStemState(s.Name, 1, false, false)).ToArray();
        Configure(Settings with { TempoPercent = 0, Bpm = track.Bpm, BeatOffset = track.BeatOffset });
        ResetDsp();
    }

    public void Configure(DjDeckSettings settings)
    {
        static double Clamp(double value, double min, double max) => double.IsFinite(value) ? Math.Clamp(value, min, max) : min;
        var previous = Settings;
        Settings = settings with
        {
            TempoPercent = Clamp(settings.TempoPercent, -50, 50), Bpm = Clamp(settings.Bpm, 30, 300),
            BeatOffset = Clamp(settings.BeatOffset, 0, Math.Max(Duration, 0)),
            GainDb = Clamp(settings.GainDb, -24, 12), Fader = Clamp(settings.Fader, 0, 1),
            LowDb = Clamp(settings.LowDb, -60, 12), MidDb = Clamp(settings.MidDb, -60, 12),
            HighDb = Clamp(settings.HighDb, -60, 12), Filter = Clamp(settings.Filter, -1, 1),
        };
        for (var ch = 0; ch < 2; ch++)
        {
            _eq[ch, 0].SetCoefficients(EqFilterType.LowShelf, DjTrack.SampleRate, 250, .707, Settings.LowDb);
            _eq[ch, 1].SetCoefficients(EqFilterType.Peak, DjTrack.SampleRate, 1000, .7, Settings.MidDb);
            _eq[ch, 2].SetCoefficients(EqFilterType.HighShelf, DjTrack.SampleRate, 4000, .707, Settings.HighDb);
            var f = Settings.Filter;
            _eq[ch, 3].SetCoefficients(f < 0 ? EqFilterType.LowPass : EqFilterType.HighPass, DjTrack.SampleRate,
                f < 0 ? 20000 * Math.Pow(0.004, -f) : 30 * Math.Pow(500, f), .707, 0);
        }
        if (previous.TempoPercent != Settings.TempoPercent || previous.KeyLock != Settings.KeyLock) ResetPitch();
    }

    public void Toggle()
    {
        if (_track is null) throw new InvalidOperationException("Load a track first.");
        if (Position >= Duration) Seek(_cue);
        Playing = !Playing;
    }
    public void Pause() => Playing = false;
    public void Stop() { Playing = false; Seek(_cue); }
    public void Seek(double seconds)
    {
        if (!double.IsFinite(seconds)) return;
        _frame = Math.Clamp(seconds, 0, Duration) * DjTrack.SampleRate;
        ResetDsp();
    }
    public void SetCue() => _cue = Quantized(Position);
    public void ReturnToCue() { Playing = false; Seek(_cue); }
    public void HotCue(int index, bool clear = false)
    {
        if (_track is null) return;
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual((uint)index, 4u);
        if (clear) _hotCues[index] = null;
        else if (_hotCues[index] is { } position) Seek(position);
        else _hotCues[index] = Quantized(Position);
    }
    public void RestoreCues(double cue, double?[] cues)
    {
        _cue = double.IsFinite(cue) ? Math.Clamp(cue, 0, Duration) : 0;
        for (var i = 0; i < 4; i++) _hotCues[i] = i < cues.Length && cues[i] is { } t && double.IsFinite(t) && t >= 0 && t < Duration ? t : null;
    }
    private double Quantized(double seconds) => Settings.Quantize
        ? Math.Clamp(Settings.BeatOffset + Math.Round((seconds - Settings.BeatOffset) * Settings.Bpm / 60) * 60 / Settings.Bpm, 0, Duration)
        : seconds;
    public void BeatJump(int beats) => Seek(Position + beats * 60 / Settings.Bpm);
    public void Nudge(double amount) { _nudge = 1 + Math.Clamp(amount, -.08, .08); _nudgeFrames = DjTrack.SampleRate / 4; }
    public void SetLoopIn() { _loopIn = Quantized(Position); LoopEnabled = false; }
    public void SetLoopOut()
    {
        _loopOut = Quantized(Position);
        if (_loopIn is null || _loopOut - _loopIn < .02) throw new InvalidOperationException("Loop out must come after loop in.");
        LoopEnabled = true;
    }
    public void BeatLoop(double beats)
    {
        if (_track is null) return;
        var start = Quantized(Position);
        var length = Math.Clamp(beats, .25, 64) * 60 / Settings.Bpm;
        if (start + length > Duration) start = Math.Max(0, Duration - length);
        _loopIn = start; _loopOut = Math.Min(Duration, start + length);
        LoopEnabled = _loopOut - _loopIn >= .02;
        Seek(start);
    }
    public void ToggleLoop() => LoopEnabled = !LoopEnabled && _loopIn is not null && _loopOut - _loopIn >= .02;
    public void ResizeLoop(double factor)
    {
        if (_loopIn is not { } start || _loopOut is not { } end) return;
        _loopOut = Math.Clamp(start + (end - start) * factor, start + .02, Math.Max(start + .02, Duration));
    }
    public void SetStem(int index, double volume, bool muted, bool solo)
    {
        if ((uint)index >= _stems.Length) return;
        _stems[index] = _stems[index] with { Volume = double.IsFinite(volume) ? Math.Clamp(volume, 0, 1.5) : 0, Muted = muted, Solo = solo };
    }

    public int Read(float[] buffer, int offset, int count)
    {
        if (!Playing || _track is null) { Array.Clear(buffer, offset, count); Peak = 0; return count; }
        _pitch.Read(buffer, offset, count);
        var gain = Math.Pow(10, Settings.GainDb / 20);
        float peak = 0;
        for (var i = 0; i < count; i++)
        {
            var ch = i % 2;
            var sample = buffer[offset + i];
            for (var band = 0; band < 3; band++) sample = _eq[ch, band].Process(sample);
            if (Math.Abs(Settings.Filter) > .01) sample = _eq[ch, 3].Process(sample);
            if (ch == 0) _envelope = Math.Min(1, _envelope + 1.0 / 240);
            sample *= (float)(gain * _envelope);
            buffer[offset + i] = float.IsFinite(sample) ? sample : 0;
            peak = Math.Max(peak, Math.Abs(buffer[offset + i]));
        }
        Peak = peak;
        return count;
    }

    private int ReadRaw(float[] buffer, int offset, int count)
    {
        Array.Clear(buffer, offset, count);
        if (_track is null) return count;
        var anySolo = _stems.Any(s => s.Solo);
        var end = Duration * DjTrack.SampleRate;
        for (var i = 0; i + 1 < count; i += 2)
        {
            if (LoopEnabled && _loopIn is { } start && _loopOut is { } stop && _frame >= stop * DjTrack.SampleRate)
                _frame = start * DjTrack.SampleRate + (_frame - start * DjTrack.SampleRate) % ((stop - start) * DjTrack.SampleRate);
            if (_frame >= end) { Playing = false; _frame = end; break; }
            var frame = (long)_frame;
            var fraction = (float)(_frame - frame);
            for (var stem = 0; stem < _stems.Length; stem++)
            {
                var state = _stems[stem];
                if (state.Muted || (anySolo && !state.Solo)) continue;
                var asset = _track.Stems[stem];
                if (frame >= asset.Frames) continue;
                for (var ch = 0; ch < 2; ch++)
                {
                    var a = asset.Sample(frame, ch);
                    var b = asset.Sample(Math.Min(frame + 1, asset.Frames - 1), ch);
                    buffer[offset + i + ch] += (a + (b - a) * fraction) * (float)state.Volume;
                }
            }
            _frame += Rate * (_nudgeFrames > 0 ? _nudge : 1);
            if (_nudgeFrames > 0) _nudgeFrames--;
        }
        return count;
    }

    private void ResetPitch() => _pitch = Settings.KeyLock && Math.Abs(Rate - 1) > .0001
        ? new SmbPitchShiftingSampleProvider(new DeckSource(this)) { PitchFactor = (float)(1 / Rate) }
        : new DeckSource(this);
    private void ResetDsp()
    {
        _envelope = 0;
        ResetPitch();
        foreach (var filter in _eq) filter.Reset();
    }
    public void Dispose() { Playing = false; _track?.Dispose(); _track = null; _stems = []; }
    private sealed class DeckSource(DjDeck deck) : ISampleProvider
    {
        public WaveFormat WaveFormat => deck.WaveFormat;
        public int Read(float[] buffer, int offset, int count) => deck.ReadRaw(buffer, offset, count);
    }
}
