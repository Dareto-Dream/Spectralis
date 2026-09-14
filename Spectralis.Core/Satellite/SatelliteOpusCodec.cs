using Concentus;
using Concentus.Enums;

namespace Spectralis.Core.Satellite;

/// <summary>
/// Real Opus encode/decode support for Satellite's audio path, built on Concentus (a pure C#
/// Opus port — no native binary to ship). Opus itself only runs at a fixed set of sample rates;
/// <see cref="SatelliteOpusEncodePipeline"/> is only usable when the source's current
/// <c>AudioEngine.EffectiveSampleRate</c> is already one of them. There's no resampler wired in
/// here today — if the engine is running at, say, 44100Hz, a receiver that asked for Opus falls
/// back to Pcm for that session (see <see cref="SatelliteSourceServer"/>). Most output devices
/// this app targets run at 48000Hz, so this covers the common case; a real resampler stage is
/// the honest follow-up if 44.1kHz-only rigs turn out to matter in practice.
/// </summary>
public static class SatelliteOpusCodec
{
    private static readonly int[] SupportedSampleRates = [8000, 12000, 16000, 24000, 48000];

    public const int FrameDurationMs = 20;

    /// <summary>Max bytes a single Opus packet at our bitrate/frame size could plausibly need —
    /// generous headroom, not a hard protocol spec limit.</summary>
    public const int MaxPacketBytes = 4000;

    public static bool IsRateSupported(int sampleRate) => SupportedSampleRates.Contains(sampleRate);

    /// <summary>Samples-per-channel in one 20ms Opus frame at <paramref name="sampleRate"/>. Only
    /// meaningful when <see cref="IsRateSupported"/> is true (all five supported rates divide
    /// evenly by 50).</summary>
    public static int FrameSizePerChannel(int sampleRate) => sampleRate / (1000 / FrameDurationMs);

    internal static IOpusEncoder CreateEncoder(int sampleRate, int channels)
    {
        var encoder = OpusCodecFactory.CreateEncoder(sampleRate, channels, OpusApplication.OPUS_APPLICATION_AUDIO);
        encoder.Bitrate = 128_000;
        return encoder;
    }

    internal static IOpusDecoder CreateDecoder(int sampleRate, int channels) =>
        OpusCodecFactory.CreateDecoder(sampleRate, channels);
}

/// <summary>
/// Source-side: buffers arbitrary-sized interleaved PCM blocks (NAudio hands them over at its
/// own natural block size, not aligned to Opus's fixed frame size) and emits zero or more
/// complete 20ms Opus packets per call. One instance per connected receiver that negotiated
/// Opus — Opus encoders carry running internal state, so the same stream must always go through
/// the same encoder instance, and different receivers can join/leave independently.
/// </summary>
public sealed class SatelliteOpusEncodePipeline
{
    private readonly IOpusEncoder _encoder;
    private readonly int _channels;
    private readonly int _frameSizePerChannel;
    private float[] _pending = [];
    private int _pendingLength;

    public SatelliteOpusEncodePipeline(int sampleRate, int channels)
    {
        if (!SatelliteOpusCodec.IsRateSupported(sampleRate))
        {
            throw new ArgumentException($"{sampleRate}Hz is not a native Opus rate", nameof(sampleRate));
        }

        _channels = channels;
        _frameSizePerChannel = SatelliteOpusCodec.FrameSizePerChannel(sampleRate);
        _encoder = SatelliteOpusCodec.CreateEncoder(sampleRate, channels);
        _pending = new float[_frameSizePerChannel * channels * 4];
    }

    public int FrameSizePerChannel => _frameSizePerChannel;

    /// <summary>Appends <paramref name="interleavedPcm"/> to the internal buffer and returns one
    /// encoded packet per complete frame now available (zero, one, or more). Leftover samples
    /// that don't fill a whole frame stay buffered for the next call.</summary>
    public List<byte[]> Encode(ReadOnlySpan<float> interleavedPcm)
    {
        EnsurePendingCapacity(_pendingLength + interleavedPcm.Length);
        interleavedPcm.CopyTo(_pending.AsSpan(_pendingLength));
        _pendingLength += interleavedPcm.Length;

        var frameFloats = _frameSizePerChannel * _channels;
        var packets = new List<byte[]>();
        var outBuf = new byte[SatelliteOpusCodec.MaxPacketBytes];
        var consumed = 0;

        while (_pendingLength - consumed >= frameFloats)
        {
            var frame = _pending.AsSpan(consumed, frameFloats);
            var encodedLen = _encoder.Encode(frame, _frameSizePerChannel, outBuf, outBuf.Length);
            packets.Add(outBuf[..encodedLen]);
            consumed += frameFloats;
        }

        if (consumed > 0)
        {
            var remaining = _pendingLength - consumed;
            Array.Copy(_pending, consumed, _pending, 0, remaining);
            _pendingLength = remaining;
        }

        return packets;
    }

    private void EnsurePendingCapacity(int needed)
    {
        if (_pending.Length >= needed)
        {
            return;
        }

        var grown = new float[Math.Max(needed, _pending.Length * 2)];
        Array.Copy(_pending, grown, _pendingLength);
        _pending = grown;
    }
}

/// <summary>
/// Receiver-side counterpart: decodes Opus packets from one source stream back into interleaved
/// float PCM. Like the encoder, this carries running decoder state — one instance per source
/// connection, reused across every packet from that source, never recreated per-packet.
/// </summary>
public sealed class SatelliteOpusDecodePipeline
{
    private readonly IOpusDecoder _decoder;
    private readonly int _channels;

    public SatelliteOpusDecodePipeline(int sampleRate, int channels)
    {
        _channels = channels;
        _decoder = SatelliteOpusCodec.CreateDecoder(sampleRate, channels);
    }

    public float[] Decode(ReadOnlySpan<byte> opusPacket, int frameSizePerChannel)
    {
        var outBuf = new float[frameSizePerChannel * _channels];
        var decodedPerChannel = _decoder.Decode(opusPacket, outBuf, frameSizePerChannel, false);
        return decodedPerChannel == frameSizePerChannel ? outBuf : outBuf[..(decodedPerChannel * _channels)];
    }
}
