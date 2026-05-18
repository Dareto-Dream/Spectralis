using System;

namespace Spectralis.Core.SharedPlay
{
    public class SharedPlayMessage
    {
        public string Type { get; set; } = string.Empty;
        public string UserId { get; set; } = string.Empty;
        public string? Content { get; set; }
        public DateTimeOffset SentAt { get; set; } = DateTimeOffset.UtcNow;
    }

    public class SharedPlayChatMessage : SharedPlayMessage
    {
        public SharedPlayChatMessage() => Type = "chat";
        public string DisplayName { get; set; } = string.Empty;
    }

    public class SharedPlayReactionMessage : SharedPlayMessage
    {
        public SharedPlayReactionMessage() => Type = "reaction";
        public string Emoji { get; set; } = string.Empty;
        public double TrackPositionSeconds { get; set; }
    }
}
