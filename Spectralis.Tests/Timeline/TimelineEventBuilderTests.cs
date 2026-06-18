using System;
using Spectralis.Core.Lyrics;
using Spectralis.Core.Timeline;
using Xunit;

namespace Spectralis.Tests.Timeline
{
    public class TimelineEventBuilderTests
    {
        [Fact]
        public void FromLrcFile_CreatesOneEventPerLine()
        {
            var lrc = new LrcFile();
            lrc.Lines.Add(new LrcLine { Timestamp = TimeSpan.FromSeconds(0), Text = "Intro" });
            lrc.Lines.Add(new LrcLine { Timestamp = TimeSpan.FromSeconds(5), Text = "Verse 1" });
            lrc.Lines.Add(new LrcLine { Timestamp = TimeSpan.FromSeconds(10), Text = "Chorus" });

            var events = TimelineEventBuilder.FromLrcFile(lrc);
            Assert.Equal(3, events.Count);
        }

        [Fact]
        public void FromLrcFile_EventType_IsLyricsLine()
        {
            var lrc = new LrcFile();
            lrc.Lines.Add(new LrcLine { Timestamp = TimeSpan.Zero, Text = "Line" });

            var events = TimelineEventBuilder.FromLrcFile(lrc);
            Assert.Equal("lyrics.line", events[0].EventType);
        }

        [Fact]
        public void FromLrcFile_EventStartTime_MatchesTimestamp()
        {
            var lrc = new LrcFile();
            lrc.Lines.Add(new LrcLine { Timestamp = TimeSpan.FromSeconds(3.5), Text = "Start" });

            var events = TimelineEventBuilder.FromLrcFile(lrc);
            Assert.Equal(TimeSpan.FromSeconds(3.5), events[0].StartTime);
        }

        [Fact]
        public void FromLrcFile_EmptyLrc_ReturnsEmpty()
        {
            var lrc = new LrcFile();
            var events = TimelineEventBuilder.FromLrcFile(lrc);
            Assert.Empty(events);
        }
    }
}
