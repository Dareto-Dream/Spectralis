using System;
using Spectralis.Core.Audio;

namespace Spectralis.App.Services
{
    public class NowPlayingBroadcaster : IDisposable
    {
        private readonly DiscordRpcService _discord;
        private readonly OBSOverlayServer _obs;

        public NowPlayingBroadcaster(DiscordRpcService discord, OBSOverlayServer obs)
        {
            _discord = discord;
            _obs = obs;
        }

        public void Broadcast(TrackInfo track, TimeSpan position, TimeSpan duration)
        {
            _discord.UpdatePresence(DiscordPresenceHelper.ForTrack(track, position, duration));
            _obs.UpdateState(new OBSOverlayState
            {
                TrackTitle = track.Title ?? string.Empty,
                Artist = track.Artist ?? string.Empty,
                Album = track.Album ?? string.Empty,
                PositionSeconds = position.TotalSeconds,
                DurationSeconds = duration.TotalSeconds,
                IsPlaying = true
            });
        }

        public void BroadcastIdle()
        {
            _discord.UpdatePresence(DiscordPresenceHelper.Idle());
            _obs.UpdateState(new OBSOverlayState());
        }

        public void Dispose() { }
    }
}
