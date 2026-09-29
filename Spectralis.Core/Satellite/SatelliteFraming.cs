using System.Buffers.Binary;

namespace Spectralis.Core.Satellite;

/// <summary>One logical unit on a Satellite connection: either a JSON control message or a
/// binary audio+FFT frame, multiplexed over a single stream (see <see cref="SatelliteFraming"/>).</summary>
public enum SatelliteFrameType : byte
{
    Control = 1,
    Audio = 2,
}

public readonly record struct SatelliteFrame(SatelliteFrameType Type, byte[] Payload);

/// <summary>
/// Length-prefixed framing shared by both the control-plane JSON messages and the binary
/// audio+FFT stream, so a single TCP connection per receiver carries both — no separate socket
/// needed. Wire shape: <c>[4-byte big-endian length][1-byte SatelliteFrameType][payload]</c>.
/// Pure stream I/O (no sockets referenced directly), so it's testable end-to-end over a
/// <see cref="MemoryStream"/> or a loopback <see cref="System.IO.Pipes.PipeStream"/> without any
/// real networking.
/// </summary>
public static class SatelliteFraming
{
    /// <summary>Generous headroom over a real audio+FFT frame — guards a corrupt or hostile
    /// length prefix from making the reader allocate something absurd.</summary>
    public const int MaxFrameBytes = 4 * 1024 * 1024;

    private const int HeaderBytes = 5;

    public static async Task WriteAsync(Stream stream, SatelliteFrameType type, ReadOnlyMemory<byte> payload, CancellationToken ct = default)
    {
        if (payload.Length > MaxFrameBytes)
        {
            throw new ArgumentOutOfRangeException(nameof(payload), $"frame payload ({payload.Length} bytes) exceeds MaxFrameBytes ({MaxFrameBytes})");
        }

        var header = new byte[HeaderBytes];
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(0, 4), payload.Length);
        header[4] = (byte)type;

        await stream.WriteAsync(header, ct).ConfigureAwait(false);
        if (payload.Length > 0)
        {
            await stream.WriteAsync(payload, ct).ConfigureAwait(false);
        }
    }

    /// <summary>Reads one frame, or returns null on a clean EOF exactly at a frame boundary
    /// (the other side closed the connection between frames — normal shutdown, not an error).
    /// Throws <see cref="EndOfStreamException"/> if the connection closes mid-frame, and
    /// <see cref="InvalidDataException"/> if the length prefix is negative or absurd.</summary>
    public static async Task<SatelliteFrame?> ReadAsync(Stream stream, CancellationToken ct = default)
    {
        var header = new byte[HeaderBytes];
        if (!await ReadExactAsync(stream, header, allowCleanEofAtStart: true, ct).ConfigureAwait(false))
        {
            return null;
        }

        var length = BinaryPrimitives.ReadInt32BigEndian(header.AsSpan(0, 4));
        if (length < 0 || length > MaxFrameBytes)
        {
            throw new InvalidDataException($"invalid Satellite frame length prefix: {length}");
        }

        var type = (SatelliteFrameType)header[4];
        var payload = length == 0 ? [] : new byte[length];
        if (length > 0)
        {
            await ReadExactAsync(stream, payload, allowCleanEofAtStart: false, ct).ConfigureAwait(false);
        }

        return new SatelliteFrame(type, payload);
    }

    /// <summary>Fills <paramref name="buffer"/> completely or throws. Returns false only when
    /// <paramref name="allowCleanEofAtStart"/> is true and the stream ended before any byte was
    /// read at all (a legitimate "nothing more to read" signal, not a truncation).</summary>
    private static async Task<bool> ReadExactAsync(Stream stream, byte[] buffer, bool allowCleanEofAtStart, CancellationToken ct)
    {
        var offset = 0;
        while (offset < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(offset), ct).ConfigureAwait(false);
            if (read == 0)
            {
                if (offset == 0 && allowCleanEofAtStart)
                {
                    return false;
                }

                throw new EndOfStreamException("Satellite connection closed mid-frame");
            }

            offset += read;
        }

        return true;
    }
}
