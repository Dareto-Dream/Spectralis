using System;
using Spectralis.Core.Lyrics;
using Xunit;

namespace Spectralis.Tests.Lyrics
{
    public class LyricsSearchTests
    {
        private static LrcFile MakeLrc()
        {
            var lrc = new LrcFile();
            lrc.Lines.Add(new LrcLine { Timestamp = TimeSpan.FromSeconds(0), Text = "hello world" });
            lrc.Lines.Add(new LrcLine { Timestamp = TimeSpan.FromSeconds(5), Text = "goodbye cruel world" });
            lrc.Lines.Add(new LrcLine { Timestamp = TimeSpan.FromSeconds(10), Text = "something different" });
            return lrc;
        }

        [Fact]
        public void Search_ExactMatch_FindsLine()
        {
            var index = new LrcSearchIndex();
            index.Build(MakeLrc());
            var results = index.Search("hello");
            Assert.Single(results);
            Assert.Equal("hello world", results[0].Text);
        }

        [Fact]
        public void Search_CaseInsensitive_Matches()
        {
            var index = new LrcSearchIndex();
            index.Build(MakeLrc());
            var results = index.Search("WORLD");
            Assert.Equal(2, results.Count);
        }

        [Fact]
        public void Search_NoMatch_ReturnsEmpty()
        {
            var index = new LrcSearchIndex();
            index.Build(MakeLrc());
            var results = index.Search("xyzabc");
            Assert.Empty(results);
        }

        [Fact]
        public void Search_EmptyQuery_ReturnsAll()
        {
            var index = new LrcSearchIndex();
            index.Build(MakeLrc());
            var results = index.Search("");
            Assert.Equal(3, results.Count);
        }
    }
}
