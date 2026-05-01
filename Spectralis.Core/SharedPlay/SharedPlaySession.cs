using System;
using System.Collections.Generic;

namespace Spectralis.Core.SharedPlay
{
    public class SharedPlaySession
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string HostUserId { get; set; } = string.Empty;
        public string RoomCode { get; set; } = string.Empty;
        public SharedPlayState State { get; set; } = SharedPlayState.Idle;
        public string? CurrentTrackId { get; set; }
        public TimeSpan Position { get; set; }
        public bool IsPlaying { get; set; }
        public List<string> ListenerIds { get; set; } = new();
        public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
        public DateTimeOffset LastSync { get; set; } = DateTimeOffset.UtcNow;
    }

    public enum SharedPlayState
    {
        Idle,
        Active,
        Paused,
        Ended
    }

    public class SharedPlaySyncPacket
    {
        public string SessionId { get; set; } = string.Empty;
        public string? TrackId { get; set; }
        public double PositionSeconds { get; set; }
        public bool IsPlaying { get; set; }
        public long ServerTimestampMs { get; set; }
        public string Event { get; set; } = "sync";
    }
}
