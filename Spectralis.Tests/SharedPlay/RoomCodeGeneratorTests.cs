using Spectralis.Core.SharedPlay;
using Xunit;

namespace Spectralis.Tests.SharedPlay
{
    public class RoomCodeGeneratorTests
    {
        [Fact]
        public void Generate_DefaultLength_Returns6Chars()
        {
            string code = RoomCodeGenerator.Generate();
            Assert.Equal(6, code.Length);
        }

        [Fact]
        public void Generate_CustomLength_ReturnsCorrectLength()
        {
            string code = RoomCodeGenerator.Generate(8);
            Assert.Equal(8, code.Length);
        }

        [Fact]
        public void Generate_TwoCodes_AreDistinct()
        {
            string a = RoomCodeGenerator.Generate();
            string b = RoomCodeGenerator.Generate();
            Assert.NotEqual(a, b);
        }

        [Fact]
        public void IsValid_ReturnsTrueForValidCode()
        {
            string code = RoomCodeGenerator.Generate();
            Assert.True(RoomCodeGenerator.IsValid(code));
        }

        [Fact]
        public void IsValid_ReturnsFalseForTooShort()
        {
            Assert.False(RoomCodeGenerator.IsValid("AB"));
        }
    }
}
