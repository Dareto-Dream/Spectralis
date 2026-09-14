using System.Buffers.Binary;

namespace Spectralis.Core.Satellite;

/// <summary>
/// One packet of audio + (optional) FFT data, as carried inside a <see cref="SatelliteFrameType.Audio"/>
/// frame. PCM only today — <see cref="SatelliteCodec.Opus"/> is declared in the capability
/// handshake but not actually encoded anywhere yet (see SatelliteCodec's doc comment).
///
/// Wire shape (all multi-byte fields big-endian):
/// <code>
/// [8]  SourceClockMs      (double)  — source's own clock reading when this frame was captured
/// [4]  SampleRate         (int32)
/// [4]  ChannelCount       (int32)
/// [4]  PcmSampleCount     (int32)   — total interleaved float samples, not frames
/// [4 * PcmSampleCount]     PCM samples (float32)
/// [4]  FftBinCount        (int32)   — 0 if the receiver declared display=none (see SatelliteDisplay)
/// [4 * FftBinCount]        FFT magnitude bins (float32)
/// </code>
/// </summary>
public sealed class SatelliteAudioFrame
{
    public required double SourceClockMs { get; init; }
    public required int SampleRate { get; init; }
    public required int ChannelCount { get; init; }
    public required float[] PcmSamples { get; init; }
    public required float[] FftBins { get; init; }

    public byte[] Encode()
    {
        var totalBytes = 8 + 4 + 4 + 4 + PcmSamples.Length * 4 + 4 + FftBins.Length * 4;
        var buffer = new byte[totalBytes];
        var span = buffer.AsSpan();

        BinaryPrimitives.WriteDoubleBigEndian(span, SourceClockMs);
        span = span[8..];
        BinaryPrimitives.WriteInt32BigEndian(span, SampleRate);
        span = span[4..];
        BinaryPrimitives.WriteInt32BigEndian(span, ChannelCount);
        span = span[4..];
        BinaryPrimitives.WriteInt32BigEndian(span, PcmSamples.Length);
        span = span[4..];

        foreach (var sample in PcmSamples)
        {
            BinaryPrimitives.WriteSingleBigEndian(span, sample);
            span = span[4..];
        }

        BinaryPrimitives.WriteInt32BigEndian(span, FftBins.Length);
        span = span[4..];

        foreach (var bin in FftBins)
        {
            BinaryPrimitives.WriteSingleBigEndian(span, bin);
            span = span[4..];
        }

        return buffer;
    }

    /// <summary>Returns null (never throws) on malformed input — a truncated or corrupt frame
    /// is dropped, same non-fatal handling as every other untrusted-input boundary here.</summary>
    public static SatelliteAudioFrame? Decode(ReadOnlySpan<byte> data)
    {
        try
        {
            if (data.Length < 20)
            {
                return null;
            }

            var sourceClockMs = BinaryPrimitives.ReadDoubleBigEndian(data);
            data = data[8..];
            var sampleRate = BinaryPrimitives.ReadInt32BigEndian(data);
            data = data[4..];
            var channelCount = BinaryPrimitives.ReadInt32BigEndian(data);
            data = data[4..];
            var pcmCount = BinaryPrimitives.ReadInt32BigEndian(data);
            data = data[4..];

            if (pcmCount < 0 || data.Length < pcmCount * 4L + 4)
            {
                return null;
            }

            var pcmSamples = new float[pcmCount];
            for (var i = 0; i < pcmCount; i++)
            {
                pcmSamples[i] = BinaryPrimitives.ReadSingleBigEndian(data);
                data = data[4..];
            }

            var fftCount = BinaryPrimitives.ReadInt32BigEndian(data);
            data = data[4..];

            if (fftCount < 0 || data.Length < fftCount * 4L)
            {
                return null;
            }

            var fftBins = new float[fftCount];
            for (var i = 0; i < fftCount; i++)
            {
                fftBins[i] = BinaryPrimitives.ReadSingleBigEndian(data);
                data = data[4..];
            }

            return new SatelliteAudioFrame
            {
                SourceClockMs = sourceClockMs,
                SampleRate = sampleRate,
                ChannelCount = channelCount,
                PcmSamples = pcmSamples,
                FftBins = fftBins,
            };
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
    }
}
