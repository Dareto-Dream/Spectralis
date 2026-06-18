using System;
using System.Threading.Tasks;
using Spectralis.Core.Models;

namespace Spectralis.Core.Audio
{
    public interface IAudioEngine : IDisposable
    {
        TrackInfo? CurrentTrack { get; }
        TimeSpan Position { get; }
        TimeSpan Duration { get; }
        float Volume { get; set; }
        bool IsPlaying { get; }
        bool IsPaused { get; }
        PlaybackState State { get; }

        event EventHandler? PlaybackStarted;
        event EventHandler? PlaybackPaused;
        event EventHandler? PlaybackStopped;
        event EventHandler? TrackEnded;
        event EventHandler<TrackInfo>? TrackLoaded;

        Task LoadAsync(string path);
        Task LoadStreamAsync(string url, string? mimeHint = null);
        void Play();
        void Pause();
        void Stop();
        void Seek(TimeSpan position);
    }
}
