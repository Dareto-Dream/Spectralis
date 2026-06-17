using System;

namespace Spectralis.Core.Lyrics
{
    public enum LrcTokenType { Metadata, TimedLine, Enhanced, Comment }

    public class LrcToken
    {
        public LrcTokenType Type { get; init; }
        public TimeSpan Timestamp { get; init; }
        public string Text { get; init; } = string.Empty;
        public string? MetadataKey { get; init; }
        public string? MetadataValue { get; init; }
    }
}
