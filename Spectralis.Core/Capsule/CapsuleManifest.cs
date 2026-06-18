using System;
using System.Collections.Generic;

namespace Spectralis.Core.Capsule
{
    public class CapsuleManifest
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Title { get; set; } = string.Empty;
        public string Artist { get; set; } = string.Empty;
        public string Version { get; set; } = "5.0";
        public string EntryPoint { get; set; } = "world.html";
        public List<CapsuleTrack> Tracks { get; set; } = new();
        public CapsuleTrust Trust { get; set; } = new();
        public Dictionary<string, string> Meta { get; set; } = new();
    }

    public class CapsuleTrack
    {
        public string Id { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Artist { get; set; } = string.Empty;
        public string AudioPath { get; set; } = string.Empty;
        public string? LrcPath { get; set; }
        public string? CoverPath { get; set; }
        public int DurationSeconds { get; set; }
        public Dictionary<string, string> Meta { get; set; } = new();
    }

    public class CapsuleTrust
    {
        public string? PublicKeyBase64 { get; set; }
        public string? SignatureBase64 { get; set; }
        public string? SignedAt { get; set; }
        public bool IsVerified { get; set; }
    }
}
