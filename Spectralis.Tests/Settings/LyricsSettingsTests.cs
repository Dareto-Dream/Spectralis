using Spectralis.Core.Settings;
using Xunit;

namespace Spectralis.Tests.Settings
{
    public class LyricsSettingsTests
    {
        [Fact]
        public void Defaults_AreReasonable()
        {
            var settings = new LyricsSettings();
            Assert.True(settings.Enabled);
            Assert.Equal(22, settings.FontSize);
            Assert.Equal("#FF6EC7", settings.HighlightColor);
            Assert.Equal(0, settings.SyncOffsetMs);
        }

        [Fact]
        public void ShowWordHighlight_DefaultTrue()
        {
            var settings = new LyricsSettings();
            Assert.True(settings.ShowWordHighlight);
        }

        [Fact]
        public void SyncOffset_CanBeNegative()
        {
            var settings = new LyricsSettings { SyncOffsetMs = -200 };
            Assert.Equal(-200, settings.SyncOffsetMs);
        }

        [Fact]
        public void FontSize_CanBeChanged()
        {
            var settings = new LyricsSettings { FontSize = 28 };
            Assert.Equal(28, settings.FontSize);
        }
    }
}
