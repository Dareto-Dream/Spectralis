using Spectralis.Core.Visualizers;

namespace Spectralis.App.VideoExport;

public sealed class VideoExportOptions
{
    public int Width { get; set; } = 1280;
    public int Height { get; set; } = 720;
    public int FrameRate { get; set; } = 30;
    public VisualizerMode Mode { get; set; } = VisualizerMode.MirrorSpectrum;
    public string OutputPath { get; set; } = "";
}
