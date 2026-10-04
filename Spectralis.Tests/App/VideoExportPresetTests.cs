using Spectralis.App.VideoExport;
using Xunit;

namespace Spectralis.Tests.App;

public class VideoExportPresetTests
{
    [Fact]
    public void Vertical_presets_are_9_by_16()
    {
        foreach (var p in new[] { VideoExportPreset.TikTok, VideoExportPreset.Shorts })
            Assert.Equal(9.0 / 16.0, (double)p.Width / p.Height, 3);
    }

    [Fact]
    public void ApplyTo_sets_format_but_keeps_overlay_and_output()
    {
        var options = new VideoExportOptions { OutputPath = "x.mp4", ShowAlbum = true };

        VideoExportPreset.Shorts.ApplyTo(options);

        Assert.Equal(1080, options.Width);
        Assert.Equal(1920, options.Height);
        Assert.Equal(180, options.MaxDurationSeconds);
        Assert.Equal("x.mp4", options.OutputPath);
        Assert.True(options.ShowAlbum);
    }

    [Fact]
    public void Switching_to_a_preset_without_a_cap_clears_the_cap()
    {
        var options = new VideoExportOptions();
        VideoExportPreset.Shorts.ApplyTo(options);
        VideoExportPreset.UltraHd.ApplyTo(options);

        Assert.Equal(3840, options.Width);
        Assert.Null(options.MaxDurationSeconds);
    }

    [Fact]
    public void Find_is_case_insensitive_and_ids_are_unique()
    {
        Assert.Same(VideoExportPreset.TikTok, VideoExportPreset.Find("TikTok"));
        Assert.Null(VideoExportPreset.Find("nope"));
        Assert.Equal(VideoExportPreset.All.Count, VideoExportPreset.All.Select(p => p.Id).Distinct().Count());
    }

    [Theory]
    [InlineData(240.0, 180, 180.0)]
    [InlineData(90.0, 180, 90.0)]
    [InlineData(240.0, null, 240.0)]
    public void ClampDuration_caps_only_when_longer(double audio, int? max, double expected) =>
        Assert.Equal(expected, VideoExportEngine.ClampDuration(audio, max));
}
