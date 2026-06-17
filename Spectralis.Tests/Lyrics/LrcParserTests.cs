using System;
using Spectralis.Core.Lyrics;
using Xunit;

namespace Spectralis.Tests.Lyrics
{
    public class LrcParserTests
    {
        private readonly LrcParser _parser = new();

        [Fact]
        public void Parse_BasicLine_ReturnsCorrectTimestamp()
        {
            var lrc = _parser.Parse("[01:23.45]Hello world");
            Assert.Single(lrc.Lines);
            Assert.Equal(TimeSpan.FromMilliseconds(83450), lrc.Lines[0].Timestamp);
            Assert.Equal("Hello world", lrc.Lines[0].Text);
        }

        [Fact]
        public void Parse_MetadataLine_PopulatesMetadata()
        {
            var lrc = _parser.Parse("[ti:My Song]\n[ar:Some Artist]");
            Assert.Equal("My Song", lrc.Title);
            Assert.Equal("Some Artist", lrc.Artist);
            Assert.Empty(lrc.Lines);
        }

        [Fact]
        public void Parse_MultipleTimestamps_CreatesSeparateLines()
        {
            var lrc = _parser.Parse("[00:10.00][00:45.00]Chorus line");
            Assert.Equal(2, lrc.Lines.Count);
            Assert.Equal("Chorus line", lrc.Lines[0].Text);
            Assert.Equal("Chorus line", lrc.Lines[1].Text);
        }

        [Fact]
        public void Parse_EnhancedLrc_ParsesWords()
        {
            var lrc = _parser.Parse("[00:05.00]<00:05.00>Hello <00:05.50>world");
            Assert.Single(lrc.Lines);
            Assert.True(lrc.Lines[0].IsEnhanced);
            Assert.Equal(2, lrc.Lines[0].Words!.Count);
            Assert.Equal("Hello ", lrc.Lines[0].Words[0].Text);
        }

        [Fact]
        public void Parse_CommentLines_AreSkipped()
        {
            var lrc = _parser.Parse("// comment\n[00:01.00]Line one");
            Assert.Single(lrc.Lines);
        }

        [Fact]
        public void Parse_LinesAreSortedByTimestamp()
        {
            var lrc = _parser.Parse("[00:30.00]Second\n[00:10.00]First");
            Assert.Equal("First", lrc.Lines[0].Text);
            Assert.Equal("Second", lrc.Lines[1].Text);
        }

        [Fact]
        public void Parse_OffsetMetadata_AffectsGetCurrentLine()
        {
            var lrc = _parser.Parse("[offset:500]\n[00:01.00]Late line");
            var line = lrc.GetCurrentLine(TimeSpan.FromSeconds(1));
            Assert.Null(line);
            line = lrc.GetCurrentLine(TimeSpan.FromMilliseconds(1600));
            Assert.NotNull(line);
        }

        [Fact]
        public void ParseTimestamp_ReturnsCorrectSpan()
        {
            var ts = LrcParser.ParseTimestamp("[02:34.56]");
            Assert.Equal(2, (int)ts.TotalMinutes);
            Assert.Equal(34, ts.Seconds);
            Assert.Equal(560, ts.Milliseconds);
        }
    }
}
