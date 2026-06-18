using System;
using System.Threading.Tasks;
using Spectralis.Core.Audio;

namespace Spectralis.App.Services
{
    public class BroadcastCoordinator : IDisposable
    {
        private readonly DiscordRpcService _discord;
        private readonly OBSOverlayServer _obs;
        private readonly OBSOverlayStateBuilder _stateBuilder;
        private System.Timers.Timer? _updateTimer;

        private TrackInfo? _currentTrack;
        private Func<TimeSpan>? _positionGetter;
        private Func<TimeSpan>? _durationGetter;
        private Func<bool>? _isPlayingGetter;

        public BroadcastCoordinator(
            DiscordRpcService discord,
            OBSOverlayServer obs,
            OBSOverlayStateBuilder stateBuilder)
        {
            _discord = discord;
            _obs = obs;
            _stateBuilder = stateBuilder;
        }

        public void Start(Func<TimeSpan> position, Func<TimeSpan> duration, Func<bool> isPlaying)
        {
            _positionGetter = position;
            _durationGetter = duration;
            _isPlayingGetter = isPlaying;

            _updateTimer = new System.Timers.Timer(5000);
            _updateTimer.Elapsed += async (_, _) => await UpdateAsync();
            _updateTimer.Start();
        }

        public void SetTrack(TrackInfo track)
        {
            _currentTrack = track;
            _ = UpdateAsync();
        }

        private async Task UpdateAsync()
        {
            if (_currentTrack == null || _positionGetter == null) return;
            var pos = _positionGetter();
            var dur = _durationGetter?.Invoke() ?? TimeSpan.Zero;
            bool playing = _isPlayingGetter?.Invoke() ?? false;

            _discord.UpdatePresence(DiscordPresenceHelper.ForTrack(_currentTrack, pos, dur));
            var state = await _stateBuilder.BuildAsync(_currentTrack, pos, dur, playing);
            _obs.UpdateState(state);
        }

        public void Stop()
        {
            _updateTimer?.Stop();
            _discord.ClearPresence();
            _obs.UpdateState(new OBSOverlayState());
        }

        public void Dispose()
        {
            Stop();
            _updateTimer?.Dispose();
        }
    }
}
