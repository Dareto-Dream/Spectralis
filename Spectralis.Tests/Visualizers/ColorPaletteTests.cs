using FluentAssertions;
using SkiaSharp;
using Spectralis.Core.Visualizers;
using Xunit;

namespace Spectralis.Tests.Visualizers
{
    public class ColorPaletteTests
    {
        [Fact]
        public void Sample_ZeroT_ReturnsFirstStop()
        {
            var p = new ColorPalette(new SKColor(255, 0, 0), new SKColor(0, 0, 255));
            var c = p.Sample(0f);
            c.Red.Should().Be(255);
            c.Blue.Should().Be(0);
        }

        [Fact]
        public void Sample_OneT_ReturnsLastStop()
        {
            var p = new ColorPalette(new SKColor(255, 0, 0), new SKColor(0, 0, 255));
            var c = p.Sample(1f);
            c.Red.Should().Be(0);
            c.Blue.Should().Be(255);
        }

        [Fact]
        public void Sample_MidT_InterpolatesCorrectly()
        {
            var p = new ColorPalette(new SKColor(0, 0, 0), new SKColor(200, 0, 0));
            var c = p.Sample(0.5f);
            c.Red.Should().BeInRange(98, 102);
        }

        [Fact]
        public void Sample_OutOfRangeT_Clamped()
        {
            var p = new ColorPalette(new SKColor(255, 0, 0), new SKColor(0, 0, 255));
            p.Sample(-0.5f).Should().Be(p.Sample(0f));
            p.Sample(1.5f).Should().Be(p.Sample(1f));
        }

        [Fact]
        public void SampleWithAlpha_SetsAlpha()
        {
            var p = new ColorPalette(new SKColor(255, 128, 0));
            var c = p.SampleWithAlpha(0f, 128);
            c.Alpha.Should().Be(128);
        }

        [Fact]
        public void StaticPresets_AllHaveFourStops()
        {
            ColorPalette.Neon.Sample(0.5f);
            ColorPalette.Fire.Sample(0.5f);
            ColorPalette.Ocean.Sample(0.5f);
            ColorPalette.Monochrome.Sample(0.5f);
        }

        [Fact]
        public void All_ContainsFourPresets()
        {
            ColorPalette.All.Should().HaveCount(4);
        }

        [Fact]
        public void SingleStop_AlwaysReturnsThatStop()
        {
            var p = new ColorPalette(new SKColor(100, 150, 200));
            p.Sample(0f).Should().Be(new SKColor(100, 150, 200));
            p.Sample(0.5f).Should().Be(new SKColor(100, 150, 200));
            p.Sample(1f).Should().Be(new SKColor(100, 150, 200));
        }
    }
}
