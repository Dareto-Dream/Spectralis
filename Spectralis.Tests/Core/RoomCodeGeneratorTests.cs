using Spectralis.Core.SharedPlay;
using Xunit;

namespace Spectralis.Tests.Core
{
    public class RoomCodeLengthTests
    {
        [Theory]
        [InlineData(4)]
        [InlineData(6)]
        [InlineData(8)]
        [InlineData(12)]
        public void Generate_ReturnsRequestedLength(int length)
        {
            var code = RoomCodeGenerator.Generate(length);
            Assert.Equal(length, code.Length);
        }

        [Fact]
        public void Generate_IsUpperCase()
        {
            var code = RoomCodeGenerator.Generate(20);
            Assert.Equal(code.ToUpperInvariant(), code);
        }
    }
}
