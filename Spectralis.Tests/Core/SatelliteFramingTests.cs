using System.Buffers.Binary;
using System.Text;
using Spectralis.Core.Satellite;
using Xunit;

namespace Spectralis.Tests.Core;

public sealed class SatelliteFramingTests
{
    [Fact]
    public async Task WriteThenRead_RoundTripsControlFrame()
    {
        using var stream = new MemoryStream();
        var payload = Encoding.UTF8.GetBytes("""{"v":1,"t":"hello"}""");

        await SatelliteFraming.WriteAsync(stream, SatelliteFrameType.Control, payload);
        stream.Position = 0;

        var frame = await SatelliteFraming.ReadAsync(stream);

        Assert.NotNull(frame);
        Assert.Equal(SatelliteFrameType.Control, frame!.Value.Type);
        Assert.Equal(payload, frame.Value.Payload);
    }

    [Fact]
    public async Task WriteThenRead_RoundTripsAudioFrame()
    {
        using var stream = new MemoryStream();
        var payload = new byte[1024];
        Random.Shared.NextBytes(payload);

        await SatelliteFraming.WriteAsync(stream, SatelliteFrameType.Audio, payload);
        stream.Position = 0;

        var frame = await SatelliteFraming.ReadAsync(stream);

        Assert.NotNull(frame);
        Assert.Equal(SatelliteFrameType.Audio, frame!.Value.Type);
        Assert.Equal(payload, frame.Value.Payload);
    }

    [Fact]
    public async Task MultipleFrames_ReadBackInOrder()
    {
        using var stream = new MemoryStream();
        await SatelliteFraming.WriteAsync(stream, SatelliteFrameType.Control, "one"u8.ToArray());
        await SatelliteFraming.WriteAsync(stream, SatelliteFrameType.Audio, "two"u8.ToArray());
        await SatelliteFraming.WriteAsync(stream, SatelliteFrameType.Control, "three"u8.ToArray());
        stream.Position = 0;

        var first = await SatelliteFraming.ReadAsync(stream);
        var second = await SatelliteFraming.ReadAsync(stream);
        var third = await SatelliteFraming.ReadAsync(stream);
        var fourth = await SatelliteFraming.ReadAsync(stream);

        Assert.Equal("one", Encoding.UTF8.GetString(first!.Value.Payload));
        Assert.Equal("two", Encoding.UTF8.GetString(second!.Value.Payload));
        Assert.Equal("three", Encoding.UTF8.GetString(third!.Value.Payload));
        Assert.Null(fourth); // clean EOF at a frame boundary
    }

    [Fact]
    public async Task ZeroLengthPayload_RoundTrips()
    {
        using var stream = new MemoryStream();
        await SatelliteFraming.WriteAsync(stream, SatelliteFrameType.Control, ReadOnlyMemory<byte>.Empty);
        stream.Position = 0;

        var frame = await SatelliteFraming.ReadAsync(stream);

        Assert.NotNull(frame);
        Assert.Empty(frame!.Value.Payload);
    }

    [Fact]
    public async Task ReadAsync_EmptyStream_ReturnsNull()
    {
        using var stream = new MemoryStream();

        var frame = await SatelliteFraming.ReadAsync(stream);

        Assert.Null(frame);
    }

    [Fact]
    public async Task ReadAsync_TruncatedMidHeader_Throws()
    {
        using var stream = new MemoryStream([0x00, 0x00]); // only 2 of 5 header bytes

        await Assert.ThrowsAsync<EndOfStreamException>(() => SatelliteFraming.ReadAsync(stream));
    }

    [Fact]
    public async Task ReadAsync_TruncatedMidPayload_Throws()
    {
        using var stream = new MemoryStream();
        var header = new byte[5];
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(0, 4), 100); // claims 100 bytes
        header[4] = (byte)SatelliteFrameType.Control;
        stream.Write(header);
        stream.Write([1, 2, 3]); // but only 3 actually follow
        stream.Position = 0;

        await Assert.ThrowsAsync<EndOfStreamException>(() => SatelliteFraming.ReadAsync(stream));
    }

    [Fact]
    public async Task ReadAsync_NegativeLength_ThrowsInvalidData()
    {
        using var stream = new MemoryStream();
        var header = new byte[5];
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(0, 4), -1);
        stream.Write(header);
        stream.Position = 0;

        await Assert.ThrowsAsync<InvalidDataException>(() => SatelliteFraming.ReadAsync(stream));
    }

    [Fact]
    public async Task ReadAsync_LengthExceedsMax_ThrowsInvalidData()
    {
        using var stream = new MemoryStream();
        var header = new byte[5];
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(0, 4), SatelliteFraming.MaxFrameBytes + 1);
        stream.Write(header);
        stream.Position = 0;

        await Assert.ThrowsAsync<InvalidDataException>(() => SatelliteFraming.ReadAsync(stream));
    }

    [Fact]
    public async Task WriteAsync_OversizedPayload_ThrowsBeforeWriting()
    {
        using var stream = new MemoryStream();
        var oversized = new byte[SatelliteFraming.MaxFrameBytes + 1];

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => SatelliteFraming.WriteAsync(stream, SatelliteFrameType.Audio, oversized));

        Assert.Equal(0, stream.Length); // nothing partially written
    }
}
