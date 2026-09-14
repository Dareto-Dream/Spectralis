using Spectralis.Core.Satellite;
using Xunit;

namespace Spectralis.Tests.Core;

public sealed class SatellitePairingCodeTests
{
    [Fact]
    public void Generate_ProducesSixDigits()
    {
        for (var i = 0; i < 100; i++)
        {
            var pin = SatellitePairingCode.Generate();
            Assert.Equal(6, pin.Length);
            Assert.True(pin.All(char.IsDigit), $"pin '{pin}' contains a non-digit");
        }
    }

    [Fact]
    public void Matches_CorrectPin_ReturnsTrue()
    {
        Assert.True(SatellitePairingCode.Matches("123456", "123456"));
    }

    [Theory]
    [InlineData("123457", "123456")]
    [InlineData("12345", "123456")]
    [InlineData("", "123456")]
    [InlineData("1234567", "123456")]
    public void Matches_WrongOrMalformedPin_ReturnsFalse(string entered, string expected)
    {
        Assert.False(SatellitePairingCode.Matches(entered, expected));
    }
}
