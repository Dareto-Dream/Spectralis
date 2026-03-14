using System;
using Spectralis.Core.Lyrics;
using Xunit;

namespace Spectralis.Tests.Lyrics
{
    public class LrcExporterTests
    {
        private readonly LrcParser _parser = new();
        private readonly LrcExporter _exporter = new();

        [Fact]
        public void Export_RoundTrip_PreservesLines()
        {
            const string input = "[00:01.00]Line one\n[00:05.50]Line two";
            var lrc = _parser.Parse(input);
            string exported = _exporter.Export(lrc);
            var reimported = _parser.Parse(exported);

            Assert.Equal(lrc.Lines.Count, reimported.Lines.Count);
            Assert.Equal(lrc.Lines[0].Text, reimported.Lines[0].Text);
            Assert.Equal(lrc.Lines[1].Text, reimported.Lines[1].Text);
        }

        [Fact]
        public void Export_IncludesMetadata()
        {
            var lrc = _parser.Parse("[ti:My Song]\n[00:01.00]Line");
            string exported = _exporter.Export(lrc);
            Assert.Contains("[ti:My Song]", exported);
        }

        [Fact]
        public void Export_EnhancedLrc_WritesWordTimestamps()
        {
            var lrc = _parser.Parse("[00:05.00]<00:05.00>Hello <00:05.50>world");
            string exported = _exporter.Export(lrc);
            Assert.Contains("<", exported);
            Assert.Contains("Hello", exported);
        }
    }
}
