namespace Spectralis.Core.Visualizers;

/// <summary>Read-only visualization snapshots. Reading never advances audio playback.</summary>
public interface IVisualizerFrameSource
{
    bool HasAudio { get; }
    bool IsPlaying { get; }
    double PositionSeconds { get; }
    VisualizerFrame GetFrame(bool includeSpectrogram = false, bool includeRawFft = false);
}
