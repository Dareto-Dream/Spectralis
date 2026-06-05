using Spectralis.Core.Audio;
using Xunit;

namespace Spectralis.Tests.Audio
{
    public class WaveformBufferTests
    {
        [Fact]
        public void Write_ThenRead_ReturnsSamples()
        {
            var buf = new WaveformBuffer(256);
            var data = new float[256];
            for (int i = 0; i < 256; i++) data[i] = i / 256f;
            buf.Write(data);
            var read = buf.Read();
            Assert.Equal(256, read.Length);
        }

        [Fact]
        public void Length_ReflectsCapacity()
        {
            var buf = new WaveformBuffer(512);
            Assert.Equal(512, buf.Length);
        }

        [Fact]
        public void Write_PartialData_FillsFromStart()
        {
            var buf = new WaveformBuffer(512);
            buf.Write(new float[256]);
            var read = buf.Read();
            Assert.Equal(512, read.Length);
        }

        [Fact]
        public void InitialState_AllZero()
        {
            var buf = new WaveformBuffer(128);
            var data = buf.Read();
            Assert.All(data, v => Assert.Equal(0f, v));
        }
    }
}
