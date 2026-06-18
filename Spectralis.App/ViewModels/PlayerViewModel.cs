using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Spectralis.App.Services;
using Spectralis.Core.Models;

namespace Spectralis.App.ViewModels
{
    public partial class PlayerViewModel : ViewModelBase
    {
        private readonly ServiceContainer _services;

        [ObservableProperty] private TrackInfo? _currentTrack;
        [ObservableProperty] private PlaybackState _playbackState = PlaybackState.Stopped;
        [ObservableProperty] private TimeSpan _position;
        [ObservableProperty] private TimeSpan _duration;
        [ObservableProperty] private float _volume = 0.8f;
        [ObservableProperty] private bool _isShuffle;
        [ObservableProperty] private Core.Models.RepeatMode _repeatMode;
        [ObservableProperty] private int _queueCount;

        public PlayerViewModel(ServiceContainer services)
        {
            _services = services;

            _services.AudioEngine.TrackLoaded += (s, t) =>
            {
                CurrentTrack = t;
                Duration = t.Duration;
            };
            _services.AudioEngine.PlaybackStarted += (s, e) => PlaybackState = PlaybackState.Playing;
            _services.AudioEngine.PlaybackPaused += (s, e) => PlaybackState = PlaybackState.Paused;
            _services.AudioEngine.PlaybackStopped += (s, e) => PlaybackState = PlaybackState.Stopped;

            _services.Queue.QueueChanged += (s, e) => QueueCount = _services.Queue.Count;
            _services.Queue.CurrentChanged += (s, item) => CurrentTrack = item?.Track;
        }

        [RelayCommand]
        private void Play() => _services.AudioEngine.Play();

        [RelayCommand]
        private void Pause() => _services.AudioEngine.Pause();

        [RelayCommand]
        private void Stop() => _services.AudioEngine.Stop();

        [RelayCommand]
        private void Next()
        {
            var item = _services.Queue.Next();
            if (item != null) _ = _services.AudioEngine.LoadAsync(item.Track.FilePath).ContinueWith(_ => _services.AudioEngine.Play());
        }

        [RelayCommand]
        private void Previous()
        {
            var item = _services.Queue.Previous();
            if (item != null) _ = _services.AudioEngine.LoadAsync(item.Track.FilePath).ContinueWith(_ => _services.AudioEngine.Play());
        }

        [RelayCommand]
        private void ToggleShuffle()
        {
            IsShuffle = !IsShuffle;
            _services.Queue.SetShuffle(IsShuffle);
        }

        [RelayCommand]
        private void CycleRepeat()
        {
            RepeatMode = RepeatMode switch
            {
                Core.Models.RepeatMode.None => Core.Models.RepeatMode.RepeatAll,
                Core.Models.RepeatMode.RepeatAll => Core.Models.RepeatMode.RepeatOne,
                _ => Core.Models.RepeatMode.None
            };
            _services.Queue.RepeatMode = RepeatMode;
        }

        public void Seek(double fraction) =>
            _services.AudioEngine.Seek(TimeSpan.FromSeconds(fraction * Duration.TotalSeconds));

        partial void OnVolumeChanged(float value) =>
            _services.AudioEngine.Volume = value;
    }
}
