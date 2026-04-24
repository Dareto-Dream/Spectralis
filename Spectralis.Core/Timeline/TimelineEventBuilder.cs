using System;
using System.Collections.Generic;
using Spectralis.Core.Lyrics;

namespace Spectralis.Core.Timeline
{
    public class TimelineEventBuilder
    {
        public ReactiveTimeline FromLrcFile(LrcFile lrc, string trackId)
        {
            var timeline = new ReactiveTimeline
            {
                Name = trackId,
                Duration = lrc.Lines.Count > 0
                    ? lrc.Lines[^1].Timestamp + TimeSpan.FromSeconds(5)
                    : TimeSpan.FromMinutes(5)
            };

            var lyricsTrack = new TimelineTrack { Name = "Lyrics", Type = TimelineTrackType.Lyrics };

            for (int i = 0; i < lrc.Lines.Count; i++)
            {
                var line = lrc.Lines[i];
                var nextTs = i + 1 < lrc.Lines.Count
                    ? lrc.Lines[i + 1].Timestamp
                    : line.Timestamp + TimeSpan.FromSeconds(4);

                lyricsTrack.Events.Add(new TimelineEvent
                {
                    StartTime = line.Timestamp,
                    Duration = nextTs - line.Timestamp,
                    Type = "lyrics.line",
                    Payload = new Dictionary<string, object?> { ["text"] = line.Text }
                });
            }

            timeline.Tracks.Add(lyricsTrack);
            return timeline;
        }
    }
}
