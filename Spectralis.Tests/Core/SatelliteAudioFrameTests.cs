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
    [InlineData(16)]
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
        // Layout: [0..8) SourceClockMs, [8..12) SampleRate, [12..16) ChannelCount, [16) Encoding
        // (0 = Pcm, matches the zero-filled buffer), [17..21) PcmSampleCount.
        var buffer = new byte[25];
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(buffer.AsSpan(17), int.MaxValue / 2);

        Assert.Null(SatelliteAudioFrame.Decode(buffer));
    }

    [Fact]
    public void EncodeThenDecode_OpusFrame_RoundTripsExactly()
    {
        var original = new SatelliteAudioFrame
        {
            SourceClockMs = 987654.321,
            SampleRate = 48000,
            ChannelCount = 2,
            Encoding = SatelliteAudioEncoding.Opus,
            OpusPayload = [1, 2, 3, 4, 5, 250, 251],
            OpusFrameSize = 960,
            FftBins = [0.1f, 0.2f, 0.3f],
        };

        var decoded = SatelliteAudioFrame.Decode(original.Encode());

        Assert.NotNull(decoded);
        Assert.Equal(SatelliteAudioEncoding.Opus, decoded!.Encoding);
        Assert.Equal(original.OpusPayload, decoded.OpusPayload);
        Assert.Equal(original.OpusFrameSize, decoded.OpusFrameSize);
        Assert.Empty(decoded.PcmSamples);
        Assert.Equal(original.FftBins, decoded.FftBins);
    }

    [Fact]
    public void EncodeThenDecode_OpusFrame_EmptyOpusPayload_RoundTrips()
    {
        var original = new SatelliteAudioFrame
        {
            SourceClockMs = 1.0,
            SampleRate = 48000,
            ChannelCount = 1,
            Encoding = SatelliteAudioEncoding.Opus,
            OpusPayload = [],
            OpusFrameSize = 0,
            FftBins = [],
        };

        var decoded = SatelliteAudioFrame.Decode(original.Encode());

        Assert.NotNull(decoded);
        Assert.Equal(SatelliteAudioEncoding.Opus, decoded!.Encoding);
        Assert.Empty(decoded.OpusPayload);
    }

    [Fact]
    public void Decode_TruncatedMidOpusPayload_ReturnsNullNotThrows()
    {
        var full = new SatelliteAudioFrame
        {
            SourceClockMs = 1.0,
            SampleRate = 48000,
            ChannelCount = 2,
            Encoding = SatelliteAudioEncoding.Opus,
            OpusPayload = Enumerable.Range(0, 200).Select(i => (byte)i).ToArray(),
            OpusFrameSize = 960,
            FftBins = [],
        }.Encode();

        var truncated = full[..(full.Length - 50)];

        Assert.Null(SatelliteAudioFrame.Decode(truncated));
    }

    [Fact]
    public void Decode_UnknownEncodingByte_ReturnsNullNotThrows()
    {
        var buffer = new byte[25];
        buffer[16] = 0xEE; // neither Pcm (0) nor Opus (1)

        Assert.Null(SatelliteAudioFrame.Decode(buffer));
    }

    [Fact]
    public void WithPcmSamples_ReplacesOnlyPcmSamples()
    {
        var original = new SatelliteAudioFrame
        {
            SourceClockMs = 5.0,
            SampleRate = 48000,
            ChannelCount = 2,
            Encoding = SatelliteAudioEncoding.Opus,
            OpusPayload = [9, 9, 9],
            OpusFrameSize = 960,
            FftBins = [0.5f],
        };

        var replaced = original.WithPcmSamples([1f, 2f, 3f]);

        Assert.Equal(new[] { 1f, 2f, 3f }, replaced.PcmSamples);
        Assert.Equal(original.OpusPayload, replaced.OpusPayload);
        Assert.Equal(original.Encoding, replaced.Encoding);
        Assert.Equal(original.FftBins, replaced.FftBins);
    }
}
