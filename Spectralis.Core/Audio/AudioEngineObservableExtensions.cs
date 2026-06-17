using System;
using System.Reactive;
using System.Reactive.Linq;
using Spectralis.Core.Models;

namespace Spectralis.Core.Audio
{
    public static class AudioEngineObservableExtensions
    {
        public static IObservable<Unit> WhenPlaybackStarted(this IAudioEngine engine)
            => Observable.FromEventPattern(h => engine.PlaybackStarted += h, h => engine.PlaybackStarted -= h)
                .Select(_ => Unit.Default);

        public static IObservable<Unit> WhenPlaybackPaused(this IAudioEngine engine)
            => Observable.FromEventPattern(h => engine.PlaybackPaused += h, h => engine.PlaybackPaused -= h)
                .Select(_ => Unit.Default);

        public static IObservable<Unit> WhenPlaybackStopped(this IAudioEngine engine)
            => Observable.FromEventPattern(h => engine.PlaybackStopped += h, h => engine.PlaybackStopped -= h)
                .Select(_ => Unit.Default);

        public static IObservable<Unit> WhenTrackEnded(this IAudioEngine engine)
            => Observable.FromEventPattern(h => engine.TrackEnded += h, h => engine.TrackEnded -= h)
                .Select(_ => Unit.Default);

        public static IObservable<TrackInfo> WhenTrackLoaded(this IAudioEngine engine)
            => Observable.FromEventPattern<TrackInfo>(h => engine.TrackLoaded += h, h => engine.TrackLoaded -= h)
                .Select(e => e.EventArgs);

        public static IObservable<Unit> WhenPlayStateChanged(this IAudioEngine engine)
            => engine.WhenPlaybackStarted()
                .Merge(engine.WhenPlaybackPaused())
                .Merge(engine.WhenPlaybackStopped());
    }
}
