using Spectralis.Core.Satellite;
using Xunit;

namespace Spectralis.Tests.Core;

/// <summary>
/// Exercises the real Opus encode/decode path (Concentus, no mocking) end to end: a continuous
/// tone fed through <see cref="SatelliteOpusEncodePipeline"/> in irregularly-sized chunks (like
/// NAudio's natural block size, not aligned to Opus's fixed frame size), decoded back with
/// <see cref="SatelliteOpusDecodePipeline"/>, and compared against the original.
///
/// Opus has a small, constant algorithmic delay (encoder lookahead baked into the bitstream —
/// confirmed via a standalone spike at ~3.9ms/186 samples @ 48kHz), so decoded audio lags the
/// input by a few hundred samples. For a periodic test signal, comparing sample-by-sample
/// without accounting for that lag produces a huge apparent error even though the audio is
/// essentially intact — so these tests find the true lag via cross-correlation first, exactly
/// like the spike that validated this library before any production code was written.
/// </summary>
public sealed class SatelliteOpusCodecTests
{
    [Theory]
    [InlineData(8000)]
    [InlineData(12000)]
    [InlineData(16000)]
    [InlineData(24000)]
    [InlineData(48000)]
    public void IsRateSupported_TrueForAllNativeOpusRates(int rate)
    {
        Assert.True(SatelliteOpusCodec.IsRateSupported(rate));
    }

    [Theory]
    [InlineData(44100)]
    [InlineData(22050)]
    [InlineData(0)]
    public void IsRateSupported_FalseForNonNativeRates(int rate)
    {
        Assert.False(SatelliteOpusCodec.IsRateSupported(rate));
    }

    [Fact]
    public void EncodePipeline_Construct_RejectsUnsupportedRate()
    {
        Assert.Throws<ArgumentException>(() => new SatelliteOpusEncodePipeline(44100, 2));
    }

    [Fact]
    public void EncodePipeline_IrregularChunks_EmitsFixedSizeFramesAndBuffersRemainder()
    {
        // 48kHz stereo -> 960 samples/channel = 1920 interleaved floats per 20ms frame.
        var pipeline = new SatelliteOpusEncodePipeline(48000, 2);
        Assert.Equal(960, pipeline.FrameSizePerChannel);

        // Feed odd-sized chunks that don't line up with the frame boundary, like real NAudio
        // block sizes do, and confirm packets only come out once a full frame is buffered.
        var chunk1 = MakeStereoTone(samplesPerChannel: 500, freqHz: 440);
        var packets1 = pipeline.Encode(chunk1);
        Assert.Empty(packets1); // 500 < 960, nothing to emit yet

        var chunk2 = MakeStereoTone(samplesPerChannel: 700, freqHz: 440);
        var packets2 = pipeline.Encode(chunk2);
        // 500 + 700 = 1200 samples/channel -> exactly one full 960 frame, 240 left buffered
        Assert.Single(packets2);
        Assert.True(packets2[0].Length > 0);

        var chunk3 = MakeStereoTone(samplesPerChannel: 3000, freqHz: 440);
        var packets3 = pipeline.Encode(chunk3);
        // 240 buffered + 3000 = 3240 samples/channel -> 3 more full frames (2880), 360 left over
        Assert.Equal(3, packets3.Count);
    }

    [Fact]
    public void RoundTrip_48kHzStereo_RecoversToneWithinTolerance()
    {
        AssertRoundTripQuality(sampleRate: 48000, channels: 2);
    }

    [Fact]
    public void RoundTrip_16kHzMono_RecoversToneWithinTolerance()
    {
        AssertRoundTripQuality(sampleRate: 16000, channels: 1);
    }

    [Fact]
    public void DecodePipeline_ReusesStateAcrossConsecutivePackets()
    {
        // A fresh decoder per packet would still "work" numerically for a simple tone but isn't
        // how Opus is meant to be used - this asserts the same pipeline instance handles a
        // multi-packet stream without throwing or producing empty output for any packet.
        var encoder = new SatelliteOpusEncodePipeline(48000, 2);
        var decoder = new SatelliteOpusDecodePipeline(48000, 2);

        var tone = MakeStereoTone(samplesPerChannel: 960 * 5, freqHz: 220);
        var packets = encoder.Encode(tone);
        Assert.Equal(5, packets.Count);

        foreach (var packet in packets)
        {
            var pcm = decoder.Decode(packet, encoder.FrameSizePerChannel);
            Assert.Equal(960 * 2, pcm.Length);
            Assert.Contains(pcm, s => s != 0f);
        }
    }

    private static void AssertRoundTripQuality(int sampleRate, int channels)
    {
        var frameSize = SatelliteOpusCodec.FrameSizePerChannel(sampleRate);
        const int totalFrames = 12;

        var input = MakeTone(sampleRate, channels, frameSize * totalFrames, freqHz: 440);

        var encoder = new SatelliteOpusEncodePipeline(sampleRate, channels);
        var decoder = new SatelliteOpusDecodePipeline(sampleRate, channels);

        var decoded = new List<float>();
        var packets = encoder.Encode(input);
        Assert.Equal(totalFrames, packets.Count);
        foreach (var packet in packets)
        {
            decoded.AddRange(decoder.Decode(packet, frameSize));
        }

        // Left channel only, for a simple cross-correlation lag search.
        var origL = new double[input.Length / channels];
        var decL = new double[decoded.Count / channels];
        for (var i = 0; i < origL.Length; i++) origL[i] = input[i * channels];
        for (var i = 0; i < decL.Length; i++) decL[i] = decoded[i * channels];

        var maxLag = sampleRate / 100; // 10ms search window is comfortably more than Opus's delay
        var bestLag = 0;
        var bestScore = double.MinValue;
        for (var lag = 0; lag <= maxLag && lag < decL.Length; lag++)
        {
            double score = 0;
            var n = Math.Min(origL.Length, decL.Length - lag);
            for (var i = 0; i < n; i++) score += origL[i] * decL[i + lag];
            if (score > bestScore)
            {
                bestScore = score;
                bestLag = lag;
            }
        }

        // Skip the first frame (encoder warm-up) and the tail near the lag boundary.
        var skip = frameSize;
        var compareCount = Math.Min(origL.Length, decL.Length - bestLag) - skip - skip;
        Assert.True(compareCount > frameSize, "not enough overlap to compare after alignment");

        double err = 0;
        for (var i = skip; i < skip + compareCount; i++)
        {
            err += Math.Abs(origL[i] - decL[i + bestLag]);
        }

        var avgErr = err / compareCount;
        // Tone amplitude is 0.5 (full-scale float); a real lossy Opus round trip on a clean tone
        // at 128kbps should land well under 10% average absolute error once time-aligned.
        Assert.True(avgErr < 0.05, $"avg abs err {avgErr} too high (best lag {bestLag} samples)");
    }

    private static float[] MakeTone(int sampleRate, int channels, int samplesPerChannel, double freqHz)
    {
        var buffer = new float[samplesPerChannel * channels];
        for (var i = 0; i < samplesPerChannel; i++)
        {
            var s = (float)(Math.Sin(2 * Math.PI * freqHz * i / sampleRate) * 0.5);
            for (var c = 0; c < channels; c++)
            {
                buffer[i * channels + c] = s;
            }
        }

        return buffer;
    }

    private static float[] MakeStereoTone(int samplesPerChannel, double freqHz) =>
        MakeTone(48000, 2, samplesPerChannel, freqHz);
}
