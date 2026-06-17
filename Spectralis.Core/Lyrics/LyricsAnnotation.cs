using System;
using System.Collections.Generic;

namespace Spectralis.Core.Lyrics
{
    public class LyricsAnnotation
    {
        public string TimestampKey { get; set; } = string.Empty;
        public TimeSpan Timestamp { get; set; }
        public string LineText { get; set; } = string.Empty;
        public string Explanation { get; set; } = string.Empty;
        public List<string> Tags { get; set; } = new();
        public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    }

    public class LyricsAnnotationFile
    {
        public string TrackTitle { get; set; } = string.Empty;
        public string Artist { get; set; } = string.Empty;
        public Dictionary<string, LyricsAnnotation> Annotations { get; set; } = new();
    }
}
