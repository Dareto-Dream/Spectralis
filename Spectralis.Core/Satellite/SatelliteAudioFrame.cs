using System.Buffers.Binary;

namespace Spectralis.Core.Satellite;

/// <summary>Which encoding <see cref="SatelliteAudioFrame.PcmSamples"/>/<see cref="SatelliteAudioFrame.OpusPayload"/>
/// actually carries on the wire. Matches <see cref="SatelliteCodec"/> but is per-frame (the
/// source encodes each connected receiver's stream independently based on what it negotiated).</summary>
public enum SatelliteAudioEncoding
{
    Pcm = 0,
    Opus = 1,
}

/// <summary>
/// One packet of audio + (optional) FFT data, as carried inside a <see cref="SatelliteFrameType.Audio"/>
/// frame. Carries either raw PCM or a real Opus packet (see <see cref="SatelliteOpusCodec"/>) —
/// FFT bins are always raw floats regardless of audio encoding, since they're visualization data,
/// not the audio signal itself.
///
/// Wire shape (all multi-byte fields big-endian):
/// <code>
/// [8]  SourceClockMs      (double)  — source's own clock reading when this frame was captured
/// [4]  SampleRate         (int32)
/// [4]  ChannelCount       (int32)
/// [1]  Encoding           (byte)    — 0 = Pcm, 1 = Opus
/// -- if Encoding == Pcm --
/// [4]  PcmSampleCount     (int32)   — total interleaved float samples, not frames
/// [4 * PcmSampleCount]     PCM samples (float32)
/// -- if Encoding == Opus --
/// [4]  OpusFrameSize      (int32)   — samples-per-channel this packet decodes to
/// [4]  OpusPayloadLength  (int32)
/// [OpusPayloadLength]      Opus packet bytes
/// -- always --
/// [4]  FftBinCount        (int32)   — 0 if the receiver declared display=none (see SatelliteDisplay)
/// [4 * FftBinCount]        FFT magnitude bins (float32)
/// </code>
/// </summary>
public sealed class SatelliteAudioFrame
{
    public required double SourceClockMs { get; init; }
    public required int SampleRate { get; init; }
    public required int ChannelCount { get; init; }
    public required float[] FftBins { get; init; }

    public SatelliteAudioEncoding Encoding { get; init; } = SatelliteAudioEncoding.Pcm;
    public float[] PcmSamples { get; init; } = [];
    public byte[] OpusPayload { get; init; } = [];
    public int OpusFrameSize { get; init; }

    public SatelliteAudioFrame WithPcmSamples(float[] pcmSamples) => new()
    {
        SourceClockMs = SourceClockMs,
        SampleRate = SampleRate,
        ChannelCount = ChannelCount,
        FftBins = FftBins,
        Encoding = Encoding,
        PcmSamples = pcmSamples,
        OpusPayload = OpusPayload,
        OpusFrameSize = OpusFrameSize,
    };

    public byte[] Encode()
    {
        var totalBytes = 8 + 4 + 4 + 1
            + (Encoding == SatelliteAudioEncoding.Pcm
                ? 4 + PcmSamples.Length * 4
                : 4 + 4 + OpusPayload.Length)
            + 4 + FftBins.Length * 4;

        var buffer = new byte[totalBytes];
        var span = buffer.AsSpan();

        BinaryPrimitives.WriteDoubleBigEndian(span, SourceClockMs);
        span = span[8..];
        BinaryPrimitives.WriteInt32BigEndian(span, SampleRate);
        span = span[4..];
        BinaryPrimitives.WriteInt32BigEndian(span, ChannelCount);
        span = span[4..];
        span[0] = (byte)Encoding;
        span = span[1..];

        if (Encoding == SatelliteAudioEncoding.Pcm)
        {
            BinaryPrimitives.WriteInt32BigEndian(span, PcmSamples.Length);
            span = span[4..];

            foreach (var sample in PcmSamples)
            {
                BinaryPrimitives.WriteSingleBigEndian(span, sample);
                span = span[4..];
            }
        }
        else
        {
            BinaryPrimitives.WriteInt32BigEndian(span, OpusFrameSize);
            span = span[4..];
            BinaryPrimitives.WriteInt32BigEndian(span, OpusPayload.Length);
            span = span[4..];
            OpusPayload.CopyTo(span);
            span = span[OpusPayload.Length..];
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
            if (data.Length < 17)
            {
                return null;
            }

            var sourceClockMs = BinaryPrimitives.ReadDoubleBigEndian(data);
            data = data[8..];
            var sampleRate = BinaryPrimitives.ReadInt32BigEndian(data);
            data = data[4..];
            var channelCount = BinaryPrimitives.ReadInt32BigEndian(data);
            data = data[4..];
            var encoding = (SatelliteAudioEncoding)data[0];
            data = data[1..];

            var pcmSamples = Array.Empty<float>();
            var opusPayload = Array.Empty<byte>();
            var opusFrameSize = 0;

            if (encoding == SatelliteAudioEncoding.Pcm)
            {
                if (data.Length < 4)
                {
                    return null;
                }

                var pcmCount = BinaryPrimitives.ReadInt32BigEndian(data);
                data = data[4..];

                if (pcmCount < 0 || data.Length < pcmCount * 4L + 4)
                {
                    return null;
                }

                pcmSamples = new float[pcmCount];
                for (var i = 0; i < pcmCount; i++)
                {
                    pcmSamples[i] = BinaryPrimitives.ReadSingleBigEndian(data);
                    data = data[4..];
                }
            }
            else if (encoding == SatelliteAudioEncoding.Opus)
            {
                if (data.Length < 8)
                {
                    return null;
                }

                opusFrameSize = BinaryPrimitives.ReadInt32BigEndian(data);
                data = data[4..];
                var opusLen = BinaryPrimitives.ReadInt32BigEndian(data);
                data = data[4..];

                if (opusFrameSize < 0 || opusLen < 0 || data.Length < opusLen + 4)
                {
                    return null;
                }

                opusPayload = data[..opusLen].ToArray();
                data = data[opusLen..];
            }
            else
            {
                return null;
            }

            if (data.Length < 4)
            {
                return null;
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
                Encoding = encoding,
                PcmSamples = pcmSamples,
                OpusPayload = opusPayload,
                OpusFrameSize = opusFrameSize,
                FftBins = fftBins,
            };
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
    }
}
