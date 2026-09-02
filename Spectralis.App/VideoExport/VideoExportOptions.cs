using Spectralis.Core.Visualizers;

namespace Spectralis.App.VideoExport;

public sealed class VideoExportOptions
{
    public int Width { get; set; } = 1920;
    public int Height { get; set; } = 1080;
    public int FrameRate { get; set; } = 30;

    /// <summary>0–100 slider; mapped to an x264 CRF (higher quality → lower CRF).</summary>
    public int Quality { get; set; } = 85;

    /// <summary>The visualizer(s) to render. One entry unless <see cref="AutoCycle"/> is on.</summary>
    public IReadOnlyList<VideoExportVisualizerSelection> Visualizers { get; set; } =
        [VideoExportVisualizerSelection.BuiltIn(VisualizerMode.MirrorSpectrum)];

    public bool AutoCycle { get; set; }
    public int CycleSeconds { get; set; } = 12;

    // Overlay toggles (revived legacy layout: text card bottom-left, cover bottom-right, bar bottom).
    public bool ShowTitle { get; set; } = true;
    public bool ShowArtist { get; set; } = true;
    public bool ShowAlbum { get; set; }
    public bool ShowAlbumArt { get; set; } = true;
    public bool ShowProgressBar { get; set; } = true;

    public string OutputPath { get; set; } = "";

    /// <summary>The single selection when not cycling — the first entry.</summary>
    public VideoExportVisualizerSelection PrimaryVisualizer =>
        Visualizers.Count > 0
            ? Visualizers[0]
            : VideoExportVisualizerSelection.BuiltIn(VisualizerMode.MirrorSpectrum);

    /// <summary>x264 CRF for the chosen quality (legacy mapping: 12 best … 40 worst).</summary>
    public int Crf => Math.Clamp(12 + (int)((100 - Math.Clamp(Quality, 0, 100)) * 28.0 / 99.0), 12, 40);

    public bool AnyOverlayEnabled => ShowTitle || ShowArtist || ShowAlbum || ShowAlbumArt || ShowProgressBar;
}
