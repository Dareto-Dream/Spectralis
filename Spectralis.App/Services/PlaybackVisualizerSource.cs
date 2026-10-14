using Spectralis.App.ViewModels;
using Spectralis.Core.Visualizers;

namespace Spectralis.App.Services;

/// <summary>The main visualizer observes whichever playback source owns the audio.
/// Page windows move the existing surface; this never creates or advances an audio device.</summary>
public sealed class PlaybackVisualizerSource(NowPlayingViewModel player, DjViewModel mix) : IVisualizerFrameSource, IDisposable
{
    private readonly NowPlayingViewModel _player = player;
    private readonly DjViewModel _mix = mix;
    private VisualizerFrame? _cachedFrame;
    private long _frameTime;
    private bool _frameMix, _frameSpectrogram, _frameFft;
    public bool UsesMix => _player.IsMixSource;
    public bool HasAudio => UsesMix ? _mix.HasSession : _player.Engine.IsLoaded || _player.Engine.ExternalVisualizerSource is not null;
    public bool IsPlaying => _player.IsPlaying;
    public double PositionSeconds => UsesMix ? _mix.Mixer.RenderedSeconds : _player.Engine.GetPosition();
    public VisualizerFrame GetFrame(bool includeSpectrogram = false, bool includeRawFft = false)
    {
        // UI-thread observers share immutable snapshots within a frame. In particular,
        // opening a second spectrogram should not copy its history twice per refresh.
        var now = System.Diagnostics.Stopwatch.GetTimestamp();
        var mix = UsesMix;
        if (_cachedFrame is not null && mix == _frameMix &&
            System.Diagnostics.Stopwatch.GetElapsedTime(_frameTime, now).TotalMilliseconds < 16 &&
            (!includeSpectrogram || _frameSpectrogram) && (!includeRawFft || _frameFft)) return _cachedFrame;
        _frameTime = now; _frameMix = mix; _frameSpectrogram = includeSpectrogram; _frameFft = includeRawFft;
        return _cachedFrame = mix ? _mix.Mixer.GetVisualizerFrame(includeSpectrogram, includeRawFft)
            : _player.Engine.GetVisualizerFrame(includeSpectrogram, includeRawFft);
    }
    public void Dispose() => _cachedFrame = null;
}
