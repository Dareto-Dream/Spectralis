using Spectralis.Core.Visualizers;
using Xunit;

namespace Spectralis.Tests.Core;

public sealed class ImmersiveVisualizerTests
{
    [Theory]
    [InlineData(VisualizerMode.GpuBars, VisualizerMode.Spectrum)]
    [InlineData(VisualizerMode.GpuRadial, VisualizerMode.RadialSpectrum)]
    [InlineData(VisualizerMode.GpuTunnel, VisualizerMode.Wormhole)]
    public void Retired_modes_migrate_without_reusing_persisted_ids(VisualizerMode old, VisualizerMode replacement)
    {
        Assert.DoesNotContain(VisualizerCatalog.All, d => d.Mode == old);
        Assert.Equal(replacement, VisualizerCatalog.GetDefinition(old).Mode);
        Assert.Equal(replacement, VisualizerCatalog.GetPreferredMode(old, true));
        Assert.Equal(21, (int)VisualizerMode.GpuBars);
        Assert.Equal(1, (int)VisualizerMode.MirrorSpectrum);
    }

    [Theory]
    [InlineData(30)]
    [InlineData(60)]
    [InlineData(120)]
    public void Motion_uses_elapsed_time_and_freezes_without_stopping_audio_response(int fps)
    {
        var state = new VisualizerSceneState { Appearance = new() { MotionPercent = 50 } };
        var frame = new VisualizerFrame(Enumerable.Repeat(.6f, 64).ToArray(), new float[256], .8f, .4f);
        for (var i = 0; i < fps * 2; i++) state.UpdateFrame(frame, true, 0, VisualizerMode.Wormhole, 1f / fps);
        var before = state.CreateScene("test");
        Assert.InRange(before.MotionTimeSeconds, .999, 1.001);
        state.Appearance = state.Appearance with { ReduceMotion = true };
        state.UpdateFrame(VisualizerFrame.Empty, true, 5, VisualizerMode.Wormhole);
        Assert.Equal(before.MotionTimeSeconds, state.CreateScene("test").MotionTimeSeconds);
        Assert.True(state.CreateScene("test").RmsLevel < before.RmsLevel);
        state.Appearance = state.Appearance with { ReduceMotion = false };
        state.UpdateFrame(frame, false, 5, VisualizerMode.Wormhole);
        Assert.Equal(before.MotionTimeSeconds, state.CreateScene("test").MotionTimeSeconds);
        state.Clear();
        Assert.Equal(0, state.CreateScene("test").MotionTimeSeconds);
    }

    [Theory]
    [InlineData(VisualizerMode.Wormhole)]
    [InlineData(VisualizerMode.Starfield)]
    [InlineData(VisualizerMode.Orbital)]
    [InlineData(VisualizerMode.Silk)]
    public void Detail_levels_are_bounded_and_draw_without_native_pixel_readback(VisualizerMode mode)
    {
        var counts = new List<int>();
        foreach (var detail in Enum.GetValues<VisualizerDetail>())
        {
            var state = new VisualizerSceneState { Appearance = new() { Detail = detail, ShowHud = false } };
            state.UpdateFrame(VisualizerFrame.Empty, true, 0, mode);
            var canvas = new NullVizCanvas();
            VisualizerCatalog.GetDefinition(mode).Renderer.Draw(canvas, new(0, 0, 1920, 1080), state.CreateScene("test"));
            Assert.Equal(0, canvas.PixelBlits);
            Assert.InRange(canvas.CallCount, 3, 2000);
            counts.Add(canvas.CallCount);
        }
        Assert.True(counts[0] < counts[1] && counts[1] < counts[2]);
    }

    [Fact]
    public void Immersive_overlays_remain_readable_in_light_themes_and_can_be_hidden()
    {
        var state = new VisualizerSceneState { Palette = VisualizerPalette.Default with { HudInfoColor = VizColor.FromRgb(0, 0, 0) } };
        var renderer = VisualizerCatalog.CreateRenderer(VisualizerMode.Wormhole);
        var visible = new NullVizCanvas();
        renderer.Draw(visible, new(0, 0, 640, 360), state.CreateScene("Wormhole"));
        Assert.True(visible.TextDraws > 0);
        Assert.True(visible.LastTextColor!.Value.R > 150);
        state.Appearance = new() { ShowHud = false };
        var hidden = new NullVizCanvas();
        renderer.Draw(hidden, new(0, 0, 640, 360), state.CreateScene("Wormhole"));
        Assert.Equal(0, hidden.TextDraws);
    }

    [Theory]
    [InlineData(VisualizerMode.Waveform)]
    [InlineData(VisualizerMode.Spectrum)]
    [InlineData(VisualizerMode.MirrorSpectrum)]
    [InlineData(VisualizerMode.Spectrogram)]
    public void Previews_and_export_get_independent_history_and_scratch_buffers(VisualizerMode mode)
    {
        Assert.NotSame(VisualizerCatalog.CreateRenderer(mode), VisualizerCatalog.CreateRenderer(mode));
    }

    [Fact]
    public void Appearance_normalizes_invalid_values_and_preserves_theme_colors_by_default()
    {
        var normalized = new VisualizerAppearance { MotionPercent = -1, Detail = (VisualizerDetail)99, Colorway = (VisualizerColorway)99 }.Normalize();
        Assert.Equal(0, normalized.MotionPercent);
        Assert.Equal(VisualizerDetail.Balanced, normalized.Detail);
        Assert.Equal(VisualizerColorway.Theme, normalized.Colorway);
        Assert.Equal(VisualizerPalette.Default, normalized.Apply(VisualizerPalette.Default));
        Assert.NotEqual(VisualizerPalette.Default.BarStartColor, (normalized with { Colorway = VisualizerColorway.Aurora }).Apply(VisualizerPalette.Default).BarStartColor);
    }
}
