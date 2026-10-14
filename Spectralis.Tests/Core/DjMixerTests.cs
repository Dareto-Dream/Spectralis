using NAudio.Wave;
using Spectralis.Core.Audio.Dj;
using Xunit;

namespace Spectralis.Tests.Core;

public sealed class DjMixerTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "spectralis-dj-test-" + Guid.NewGuid().ToString("N"));
    private readonly FakeAudioDeviceEnumerator _devices = new();
    private readonly DjMixerEngine _engine;
    public DjMixerTests() { Directory.CreateDirectory(_directory); _engine = new(_devices) { MasterVolume = 1 }; }
    public void Dispose() { _engine.Dispose(); Directory.Delete(_directory, true); }
    private string Constant(float value, double seconds = 3)
    {
        var path = Path.Combine(_directory, Guid.NewGuid() + ".wav");
        using var writer = new WaveFileWriter(path, WaveFormat.CreateIeeeFloatWaveFormat(48000, 2));
        var samples = Enumerable.Repeat(value, (int)(seconds * 48000 * 2)).ToArray();
        writer.WriteSamples(samples, 0, samples.Length);
        return path;
    }
    private async Task Load(int deck, params string[] files) => _engine.LoadDeck(deck,
        await DjTrack.LoadAsync(files.Select((f, i) => new DjStemInput(f, "Stem " + i)).ToArray()));
    private float[] Render(int frames = 4800)
    {
        var samples = new float[frames * 2];
        Assert.Equal(samples.Length, _engine.Read(samples, 0, samples.Length));
        Assert.True(samples.All(float.IsFinite));
        return samples;
    }
    private void Play(int deck) => _engine.Transport(deck, DjTransport.PlayPause);

    [Fact]
    public async Task Visualizer_observers_read_the_limited_master_mix_without_advancing_playback()
    {
        await Load(0, Constant(.2f)); await Load(1, Constant(.6f)); Play(0); Play(1);
        _engine.GetVisualizerFrame(); // Start observation before the output callback.
        _engine.Crossfader = -1; Render();
        var first = _engine.GetVisualizerFrame();
        Assert.All(first.Waveform, sample => Assert.InRange(sample, .199f, .201f));
        var position = _engine.GetDeck(0).Position; var clock = _engine.RenderedSeconds;
        var second = _engine.GetVisualizerFrame(true, true);
        Assert.Equal(first.Waveform, second.Waveform); Assert.Equal(position, _engine.GetDeck(0).Position);
        Assert.Equal(clock, _engine.RenderedSeconds); Assert.NotEmpty(second.SpectrogramHistory);
        _engine.Crossfader = 1; _engine.MasterVolume = .5; Render();
        Assert.All(_engine.GetVisualizerFrame().Waveform, sample => Assert.InRange(sample, .299f, .301f));
        _engine.SetStem(1, 0, 1, true, false); Render();
        Assert.All(_engine.GetVisualizerFrame().Waveform, sample => Assert.InRange(Math.Abs(sample), 0, .00001f));
        _engine.SetStem(1, 0, 1.5, false, false); _engine.ConfigureDeck(1, new(GainDb: 12)); _engine.MasterVolume = 1; Render();
        Assert.All(_engine.GetVisualizerFrame().Waveform, sample => Assert.InRange(sample, .999f, 1));
        _engine.PauseAll(); Render();
        Assert.All(_engine.GetVisualizerFrame().Waveform, sample => Assert.Equal(0, sample));
        _engine.UnloadAll(); Assert.Equal(0, _engine.GetVisualizerFrame().PeakLevel);
    }

    [Fact]
    public async Task Crossfader_and_channel_faders_control_the_actual_mix()
    {
        await Load(0, Constant(.1f)); await Load(1, Constant(.3f)); Play(0); Play(1);
        _engine.Crossfader = -1;
        Assert.InRange(Render()[^1], .0999f, .1001f);
        _engine.Crossfader = 1;
        Assert.InRange(Render()[^1], .2999f, .3001f);
        _engine.Crossfader = 0;
        Assert.InRange(Render()[^1], .2827f, .2829f);
        _engine.ConfigureDeck(1, _engine.GetDeck(1).Settings with { Fader = 0 });
        Assert.InRange(Render()[^1], .0706f, .0708f);
    }

    [Fact]
    public async Task Stems_share_one_clock_and_mute_solo_and_gain_work()
    {
        await Load(0, Constant(.1f), Constant(.3f)); _engine.Crossfader = -1; Play(0);
        Assert.InRange(Render()[^1], .3999f, .4001f);
        _engine.SetStem(0, 1, 1, true, false);
        Assert.InRange(Render()[^1], .0999f, .1001f);
        _engine.SetStem(0, 1, .5, false, true);
        Assert.InRange(Render()[^1], .1499f, .1501f);
        Assert.InRange(_engine.GetDeck(0).Position, .29999, .30001);
        _engine.Seek(0, 1);
        Assert.InRange(Render()[^1], .1499f, .1501f);
        Assert.InRange(_engine.GetDeck(0).Position, 1.09999, 1.10001);
    }

    [Fact]
    public async Task Shorter_stems_end_cleanly_at_fractional_playback_rates()
    {
        await Load(0, Constant(.1f, 3), Constant(.4f, 1));
        _engine.ConfigureDeck(0, _engine.GetDeck(0).Settings with { TempoPercent = 23 });
        _engine.Crossfader = -1; _engine.Seek(0, 1.1); Play(0);
        var samples = Render();
        Assert.True(samples.Skip(500).All(s => Math.Abs(s - .1) < .0001));
    }

    [Fact]
    public async Task Hot_cues_and_beat_loops_are_quantized_and_sample_accurate()
    {
        await Load(0, Constant(.2f));
        _engine.ConfigureDeck(0, new(Bpm: 120, BeatOffset: .1));
        _engine.Seek(0, .62); _engine.HotCue(0, 0);
        Assert.InRange(_engine.GetDeck(0).HotCues[0]!.Value, .59999, .60001);
        _engine.Seek(0, 1.5); _engine.HotCue(0, 0);
        Assert.InRange(_engine.GetDeck(0).Position, .59999, .60001);
        _engine.BeatLoop(0, 1); Play(0);
        Render(48000);
        Assert.True(_engine.GetDeck(0).LoopEnabled);
        Assert.InRange(_engine.GetDeck(0).Position, .59999, 1.10001);
        _engine.HotCue(0, 0, clear: true);
        Assert.Null(_engine.GetDeck(0).HotCues[0]);
    }

    [Fact]
    public async Task Sync_matches_tempo_and_beat_phase_without_restarting_the_leader()
    {
        await Load(0, Constant(.1f)); await Load(1, Constant(.1f));
        _engine.ConfigureDeck(0, new(Bpm: 120, BeatOffset: .1, TempoPercent: 10));
        _engine.ConfigureDeck(1, new(Bpm: 100, BeatOffset: .2));
        _engine.Seek(0, .225); _engine.Seek(1, 1.45);
        _engine.Sync(1);
        Assert.Equal(32, _engine.GetDeck(1).Settings.TempoPercent, 6);
        Assert.Equal(.225, _engine.GetDeck(0).Position, 6);
        Assert.Equal(1.55, _engine.GetDeck(1).Position, 6);
    }

    [Fact]
    public async Task Headphones_are_pre_fader_and_do_not_advance_the_decks_twice()
    {
        await Load(0, Constant(.2f));
        _engine.ConfigureDeck(0, new(Fader: 0, HeadphoneCue: true));
        _engine.StartOutput("master", "headphones"); Play(0);
        var master = _devices.CreatedDevices[0]; var cue = _devices.CreatedDevices[1];
        Assert.All(master.Pump(4800), s => Assert.Equal(0, s));
        var position = _engine.GetDeck(0).Position;
        Assert.Contains(cue.Pump(4800), s => s > .1);
        Assert.Equal(position, _engine.GetDeck(0).Position);
        Assert.Throws<InvalidOperationException>(() => _engine.StartOutput("master", "master"));
    }

    [Fact]
    public async Task Recording_contains_the_limited_master_mix_and_finalizes_its_wav_header()
    {
        await Load(0, Constant(.9f)); await Load(1, Constant(.9f)); Play(0); Play(1);
        var output = Path.Combine(_directory, "recording.wav");
        _engine.StartRecording(output);
        var expected = Render();
        Assert.True(_engine.Clipping);
        Assert.InRange(expected.Max(), .999f, 1);
        await _engine.StopRecordingAsync();
        using var reader = new AudioFileReader(output);
        var actual = new float[expected.Length];
        Assert.Equal(actual.Length, reader.Read(actual, 0, actual.Length));
        Assert.Equal(expected, actual);
        Assert.Throws<IOException>(() => _engine.StartRecording(output));
    }

    [Theory]
    [InlineData(false, 550)]
    [InlineData(true, 440)]
    public async Task Tempo_changes_speed_and_key_lock_preserves_pitch(bool keyLock, double frequency)
    {
        var path = WavFixture.CreateSineWav(5, 48000, 2);
        try
        {
            await Load(0, path); _engine.Crossfader = -1;
            _engine.ConfigureDeck(0, new(TempoPercent: 25, KeyLock: keyLock)); Play(0);
            Render(24000);
            var audio = Render(48000);
            var crossings = 0;
            for (var i = 2; i < audio.Length; i += 2) if (audio[i - 2] <= 0 && audio[i] > 0) crossings++;
            Assert.InRange(crossings, frequency - 12, frequency + 12);
            Assert.InRange(_engine.GetDeck(0).Position, 1.85, 1.92);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task Loading_a_playing_deck_is_rejected_and_unload_releases_decoded_files()
    {
        await Load(0, Constant(.1f)); Play(0);
        using var replacement = await DjTrack.LoadAsync([new(Constant(.3f), "Replacement")]);
        Assert.Throws<InvalidOperationException>(() => _engine.LoadDeck(0, replacement));
        _engine.PauseAll(); _engine.UnloadAll();
        Assert.Null(_engine.GetDeck(0).Path);
        Assert.All(Render(), s => Assert.Equal(0, s));
    }
}
