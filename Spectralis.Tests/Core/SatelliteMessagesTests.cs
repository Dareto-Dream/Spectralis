using Spectralis.Core.Satellite;
using Xunit;

namespace Spectralis.Tests.Core;

public sealed class SatelliteMessagesTests
{
    [Fact]
    public void Serialize_UsesCamelCaseEnvelope()
    {
        var bytes = SatelliteMessageReader.Serialize(new SatelliteHelloMessage { DisplayName = "Living Room" });
        var json = System.Text.Encoding.UTF8.GetString(bytes);

        Assert.Contains("\"v\":1", json);
        Assert.Contains("\"t\":\"hello\"", json);
        Assert.Contains("\"displayName\":\"Living Room\"", json);
    }

    [Fact]
    public void PeekType_ReturnsTypeDiscriminator()
    {
        var bytes = SatelliteMessageReader.Serialize(new SatellitePairRequestMessage { Pin = "483920" });

        Assert.Equal(SatelliteProtocol.PairRequest, SatelliteMessageReader.PeekType(bytes));
    }

    [Fact]
    public void SerializeThenDeserialize_RoundTripsCapabilities()
    {
        var original = new SatelliteCapabilitiesMessage { Codec = SatelliteCodec.Opus, Display = SatelliteDisplay.Fft };
        var bytes = SatelliteMessageReader.Serialize(original);

        var restored = SatelliteMessageReader.Deserialize<SatelliteCapabilitiesMessage>(bytes);

        Assert.NotNull(restored);
        Assert.Equal(SatelliteCodec.Opus, restored!.Codec);
        Assert.Equal(SatelliteDisplay.Fft, restored.Display);
    }

    [Fact]
    public void SerializeThenDeserialize_RoundTripsClockPingPong()
    {
        var ping = new SatelliteClockPingMessage { T0 = 12345.678 };
        var restoredPing = SatelliteMessageReader.Deserialize<SatelliteClockPingMessage>(SatelliteMessageReader.Serialize(ping));
        Assert.Equal(12345.678, restoredPing!.T0, precision: 6);

        var pong = new SatelliteClockPongMessage { T0 = 1, T1 = 2, T2 = 3 };
        var restoredPong = SatelliteMessageReader.Deserialize<SatelliteClockPongMessage>(SatelliteMessageReader.Serialize(pong));
        Assert.Equal(1, restoredPong!.T0, precision: 6);
        Assert.Equal(2, restoredPong.T1, precision: 6);
        Assert.Equal(3, restoredPong.T2, precision: 6);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("[1,2,3]")]
    [InlineData("""{"noType":true}""")]
    [InlineData("""{"t":123}""")] // t must be a string
    public void PeekType_MalformedInput_ReturnsNullNotThrows(string input)
    {
        Assert.Null(SatelliteMessageReader.PeekType(System.Text.Encoding.UTF8.GetBytes(input)));
    }

    [Fact]
    public void Deserialize_MalformedInput_ReturnsNullNotThrows()
    {
        var result = SatelliteMessageReader.Deserialize<SatelliteHelloMessage>("not json"u8.ToArray());

        Assert.Null(result);
    }
}
