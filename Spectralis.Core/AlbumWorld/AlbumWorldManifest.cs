using System;
using System.Collections.Generic;

namespace Spectralis.Core.AlbumWorld
{
    public class AlbumWorldManifest
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string AlbumTitle { get; set; } = string.Empty;
        public string Artist { get; set; } = string.Empty;
        public string WorldHtml { get; set; } = "world.html";
        public string SchemaVersion { get; set; } = "1.0";
        public List<WorldTrack> Tracks { get; set; } = new();
        public List<Achievement> Achievements { get; set; } = new();
        public Dictionary<string, object?> Config { get; set; } = new();
    }

    public class WorldTrack
    {
        public string Id { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string AudioFile { get; set; } = string.Empty;
        public int Order { get; set; }
        public bool IsLocked { get; set; }
        public string? UnlockAfter { get; set; }
    }

    public class Achievement
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public bool IsHidden { get; set; }
        public string? Condition { get; set; }
    }

    public class WorldPlayStats
    {
        public string TrackId { get; set; } = string.Empty;
        public int PlayCount { get; set; }
        public TimeSpan TotalListenTime { get; set; }
        public DateTimeOffset? LastPlayed { get; set; }
        public bool Unlocked { get; set; }
    }
}
