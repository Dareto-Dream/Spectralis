using NAudio.Wave;
using Spectralis.Core.Audio;
using Xunit;

namespace Spectralis.Tests.Core;

public class TransitionSampleProviderTests
{
    private const int Rate = 1000;

    /// <summary>Constant-level mono source of a fixed length.</summary>
    private sealed class Tone(float level, int frames) : ISampleProvider
    {
        private int _read;
        public int Position => _read;
        public WaveFormat WaveFormat { get; } = WaveFormat.CreateIeeeFloatWaveFormat(Rate, 1);

        public int Read(float[] buffer, int offset, int count)
        {
            var n = Math.Min(count, frames - _read);
            for (var i = 0; i < n; i++)
                buffer[offset + i] = level;
            _read += n;
            return n;
        }
    }

    private sealed class WrongFormat : ISampleProvider
    {
        public WaveFormat WaveFormat { get; } = WaveFormat.CreateIeeeFloatWaveFormat(44100, 2);
        public int Read(float[] buffer, int offset, int count) => 0;
    }

    private static float[] ReadAll(ISampleProvider p, int total, int block = 64)
    {
        var all = new List<float>();
        var buf = new float[block];
        while (all.Count < total)
        {
            var n = p.Read(buf, 0, Math.Min(block, total - all.Count));
            if (n == 0)
                break;
            all.AddRange(buf.Take(n));
        }
        return all.ToArray();
    }

    [Fact]
    public void Without_a_next_source_it_just_plays_current_then_ends()
    {
        var t = new TransitionSampleProvider(new Tone(0.5f, 100));
        var audio = ReadAll(t, 500);

        Assert.Equal(100, audio.Length);
        Assert.All(audio, s => Assert.Equal(0.5f, s));
    }

    [Fact]
    public void Gapless_splice_has_no_silent_samples_between_tracks()
    {
        var t = new TransitionSampleProvider(new Tone(1f, 100));
        var transitions = 0;
        t.Transitioning += () => transitions++;
        Assert.True(t.QueueNext(new Tone(0.25f, 100), 0, null));

        var audio = ReadAll(t, 200, block: 64);

        Assert.Equal(200, audio.Length);
        Assert.All(audio.Take(100), s => Assert.Equal(1f, s));
        Assert.All(audio.Skip(100), s => Assert.Equal(0.25f, s));
        Assert.Equal(1, transitions);
        Assert.False(t.IsArmed);
    }

    [Fact]
    public void Crossfade_overlaps_and_keeps_constant_power()
    {
        var first = new Tone(1f, 400);
        var t = new TransitionSampleProvider(first);
        var transitioning = 0;
        var finished = 0;
        t.Transitioning += () => transitioning++;
        t.OutgoingFinished += () => finished++;
        // 100 frames of overlap = 0.1s at 1kHz; remaining = frames left in the first tone.
        Assert.True(t.QueueNext(new Tone(1f, 400), 0.1, () => (400 - first.Position) / (double)Rate));

        var audio = ReadAll(t, 700, block: 50);

        // Total length = 400 + 400 - 100 overlap.
        Assert.Equal(700, audio.Length);
        Assert.Equal(1, transitioning);
        Assert.Equal(1, finished);
        // Equal-power of two equal signals: cos+sin stays between 1 and sqrt(2), never silence.
        Assert.All(audio, s => Assert.InRange(s, 0.99f, 1.415f));
        Assert.Equal(1f, audio[100], 3);
        Assert.Equal(1f, audio[650], 3);
    }

    [Fact]
    public void Crossfade_midpoint_blends_both_sources()
    {
        var first = new Tone(1f, 400);
        var t = new TransitionSampleProvider(first);
        t.QueueNext(new Tone(-1f, 400), 0.1, () => (400 - first.Position) / (double)Rate);

        var audio = ReadAll(t, 700, block: 10);

        // Halfway through the fade the blend is cos(pi/4) - sin(pi/4) = 0.
        Assert.Equal(0f, audio[350], 1);
        Assert.True(audio[310] > 0.5f);
        Assert.True(audio[390] < -0.5f);
    }

    [Fact]
    public void Outgoing_running_dry_mid_fade_hands_over_without_a_gap()
    {
        // The remaining estimate says 0.1s (100 frames) but the source only has 40 frames left.
        var first = new Tone(1f, 40);
        var t = new TransitionSampleProvider(first);
        t.QueueNext(new Tone(0.5f, 300), 0.1, () => 0.1);

        var audio = ReadAll(t, 640, block: 20);

        // 100-frame fade + the 200 frames of the next track left after it.
        Assert.Equal(300, audio.Length);
        Assert.All(audio, s => Assert.True(Math.Abs(s) > 0.1f));
    }

    [Fact]
    public void Mismatched_format_is_refused_and_disarm_clears_the_queue()
    {
        var t = new TransitionSampleProvider(new Tone(1f, 100));

        Assert.False(t.QueueNext(new WrongFormat(), 0, null));
        Assert.True(t.QueueNext(new Tone(1f, 100), 0, null));
        t.Disarm();

        Assert.False(t.IsArmed);
        Assert.Equal(100, ReadAll(t, 300).Length);
    }
}
