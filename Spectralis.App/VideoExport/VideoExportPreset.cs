namespace Spectralis.App.VideoExport;

/// <summary>
/// One-click export targets. Applying a preset only touches size, frame rate, quality and the
/// optional length cap; visualizer, overlay toggles and output path stay as the user set them.
/// </summary>
public sealed record VideoExportPreset(
    string Id,
    string Label,
    int Width,
    int Height,
    int FrameRate,
    int Quality,
    int? MaxDurationSeconds = null)
{
    public static readonly VideoExportPreset Standard1080 = new("1080p", "YouTube 1080p", 1920, 1080, 30, 85);
    public static readonly VideoExportPreset TikTok = new("tiktok", "TikTok (9:16)", 1080, 1920, 30, 85, 600);
    public static readonly VideoExportPreset Shorts = new("shorts", "YouTube Shorts (9:16)", 1080, 1920, 30, 85, 180);
    public static readonly VideoExportPreset UltraHd = new("4k", "4K (2160p)", 3840, 2160, 30, 92);

    public static IReadOnlyList<VideoExportPreset> All { get; } = [Standard1080, TikTok, Shorts, UltraHd];

    public static VideoExportPreset? Find(string? id) =>
        All.FirstOrDefault(p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase));

    public void ApplyTo(VideoExportOptions options)
    {
        options.Width = Width;
        options.Height = Height;
        options.FrameRate = FrameRate;
        options.Quality = Quality;
        options.MaxDurationSeconds = MaxDurationSeconds;
    }

    public override string ToString() => Label;
}
