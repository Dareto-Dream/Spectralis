using System;
using Spectralis.Core.Audio;

namespace Spectralis.App.Services
{
    public static class DiscordPresenceHelper
    {
        public static DiscordPresence ForTrack(TrackInfo track, TimeSpan position, TimeSpan duration)
        {
            string detail = string.IsNullOrEmpty(track.Title) ? "Unknown Track" : track.Title;
            string state = string.IsNullOrEmpty(track.Artist) ? "Spectralis" : $"by {track.Artist}";

            DateTimeOffset? end = duration > TimeSpan.Zero
                ? DateTimeOffset.UtcNow + (duration - position)
                : null;

            return new DiscordPresence
            {
                Details = detail,
                State = state,
                LargeImageKey = "spectralis_logo",
                StartTimestamp = DateTimeOffset.UtcNow - position,
                EndTimestamp = end
            };
        }

        public static DiscordPresence Idle() => new()
        {
            Details = "Browsing library",
            State = "Spectralis",
            LargeImageKey = "spectralis_logo"
        };
    }
}
