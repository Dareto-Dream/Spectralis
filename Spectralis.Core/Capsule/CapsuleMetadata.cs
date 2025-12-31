using System;
using System.Collections.Generic;

namespace Spectralis.Core.Capsule
{
    public class CapsuleMetadata
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N")[..12];
        public string Title { get; set; } = string.Empty;
        public string Artist { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string Version { get; set; } = "4.0";
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public List<CapsuleTrackRef> Tracks { get; set; } = new();
        public Dictionary<string, string> Extra { get; set; } = new();
    }

    public class CapsuleTrackRef
    {
        public string Id { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Artist { get; set; } = string.Empty;
        public string AssetPath { get; set; } = string.Empty;
        public double DurationSeconds { get; set; }
        public string? LrcPath { get; set; }
        public string? VisualizerId { get; set; }
        public Dictionary<string, string> Meta { get; set; } = new();
    }
}
