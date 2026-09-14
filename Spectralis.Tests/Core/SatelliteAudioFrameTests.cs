using Spectralis.Core.Satellite;
using Xunit;

namespace Spectralis.Tests.Core;

public sealed class SatelliteAudioFrameTests
{
    private static SatelliteAudioFrame MakeFrame(int pcmCount = 8, int fftCount = 4) => new()
    {
        SourceClockMs = 123456.789,
        SampleRate = 48000,
        ChannelCount = 2,
        PcmSamples = Enumerable.Range(0, pcmCount).Select(i => (float)i * 0.1f - 0.4f).ToArray(),
        FftBins = Enumerable.Range(0, fftCount).Select(i => (float)i / fftCount).ToArray(),
    };

    [Fact]
    public void EncodeThenDecode_RoundTripsExactly()
    {
        var original = MakeFrame();

        var decoded = SatelliteAudioFrame.Decode(original.Encode());

        Assert.NotNull(decoded);
        Assert.Equal(original.SourceClockMs, decoded!.SourceClockMs, precision: 6);
        Assert.Equal(original.SampleRate, decoded.SampleRate);
        Assert.Equal(original.ChannelCount, decoded.ChannelCount);
        Assert.Equal(original.PcmSamples, decoded.PcmSamples);
        Assert.Equal(original.FftBins, decoded.FftBins);
    }

    [Fact]
    public void EncodeThenDecode_EmptyFftBins_RoundTrips()
    {
        // The "receiver declared display=none" case — no FFT data sent at all.
        var original = MakeFrame(pcmCount: 4, fftCount: 0);

        var decoded = SatelliteAudioFrame.Decode(original.Encode());

        Assert.NotNull(decoded);
        Assert.Empty(decoded!.FftBins);
        Assert.Equal(original.PcmSamples, decoded.PcmSamples);
    }

    [Fact]
    public void EncodeThenDecode_EmptyPcm_RoundTrips()
    {
        var original = MakeFrame(pcmCount: 0, fftCount: 4);

        var decoded = SatelliteAudioFrame.Decode(original.Encode());

        Assert.NotNull(decoded);
        Assert.Empty(decoded!.PcmSamples);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(19)]
    public void Decode_TooShortForHeader_ReturnsNull(int length)
    {
        Assert.Null(SatelliteAudioFrame.Decode(new byte[length]));
    }

    [Fact]
    public void Decode_TruncatedMidPcmSamples_ReturnsNullNotThrows()
    {
        var full = MakeFrame(pcmCount: 100, fftCount: 0).Encode();
        var truncated = full[..(full.Length / 2)];

        Assert.Null(SatelliteAudioFrame.Decode(truncated));
    }

    [Fact]
    public void Decode_TruncatedMidFftBins_ReturnsNullNotThrows()
    {
        var full = MakeFrame(pcmCount: 4, fftCount: 100).Encode();
        var truncated = full[..(full.Length - 50)];

        Assert.Null(SatelliteAudioFrame.Decode(truncated));
    }

    [Fact]
    public void Decode_AllOnesBytes_ReturnsNullNotThrows()
    {
        // 0xFF everywhere reads back as pcmCount == -1 (int32 all-ones) — a deterministic way
        // to hit the "negative count" guard, unlike random bytes which could occasionally land
        // on a small valid-looking count.
        var garbage = new byte[64];
        Array.Fill(garbage, (byte)0xFF);

        Assert.Null(SatelliteAudioFrame.Decode(garbage));
    }

    [Fact]
    public void Decode_HugePositiveCount_ReturnsNullNotThrows()
    {
        // A well-formed-looking header claiming far more samples than the buffer could hold.
        // Layout: [0..8) SourceClockMs, [8..12) SampleRate, [12..16) ChannelCount, [16..20) PcmSampleCount.
        var buffer = new byte[24];
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(buffer.AsSpan(16), int.MaxValue / 2);

        Assert.Null(SatelliteAudioFrame.Decode(buffer));
    }
}
