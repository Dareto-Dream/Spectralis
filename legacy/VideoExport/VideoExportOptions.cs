namespace Spectralis;

internal sealed class VideoExportOptions
{
    public int Width { get; set; } = 1920;
    public int Height { get; set; } = 1080;
    public int FrameRate { get; set; } = 30;
    public int Quality { get; set; } = 85;
    public VideoExportVisualizerOption? Visualizer { get; set; }
    public IReadOnlyList<VideoExportVisualizerOption> CycleVisualizers { get; set; } = Array.Empty<VideoExportVisualizerOption>();
    public bool AutoCycleVisualizers { get; set; }
    public int VisualizerCycleSeconds { get; set; } = 12;
    public MidiPlaybackInstrument MidiInstrument { get; set; } = MidiPlaybackInstrument.AcousticGrandPiano;
    public bool ShowTrackInfo { get; set; } = true;
    public bool ShowAlbumArt { get; set; } = true;
    public bool ShowPlaybackBar { get; set; } = true;
    public string OutputPath { get; set; } = "";
}
