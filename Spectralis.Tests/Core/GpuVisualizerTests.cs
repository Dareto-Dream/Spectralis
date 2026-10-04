using Spectralis.Core.Visualizers;
using Xunit;

namespace Spectralis.Tests.Core;

/// <summary>GpuVisualizers.Source is process-wide, so these tests must not run alongside each other.</summary>
[Collection("GpuVisualizerSource")]
public sealed class GpuVisualizerTests : IDisposable
{
    private sealed class FakeSource : IGpuFrameSource
    {
        public bool IsAvailable { get; set; } = true;
        public byte[]? NextFrame { get; set; }
        public bool ReturnWrongSize { get; set; }
        public int Calls { get; private set; }
        public (int Builtin, int W, int H, double Time, float[] Spectrum, float Rms, float Peak, VizColor Accent)? Last { get; private set; }

        public byte[]? RenderFrame(int builtinIndex, int width, int height, double timeSeconds,
            ReadOnlySpan<float> spectrum, float rms, float peak, VizColor accent)
        {
            Calls++;
            Last = (builtinIndex, width, height, timeSeconds, spectrum.ToArray(), rms, peak, accent);
            if (ReturnWrongSize) return new byte[3];
            return NextFrame ?? new byte[width * height * 4];
        }
    }

    private sealed class CountingRenderer : IVisualizerRenderer
    {
        public int Draws { get; private set; }
        public void Draw(IVizCanvas canvas, VizRect bounds, VisualizerScene scene) => Draws++;
    }

    private static VisualizerScene Scene()
    {
        var state = new VisualizerSceneState();
        var spectrum = Enumerable.Range(0, 64).Select(i => i / 64f).ToArray();
        state.UpdateFrame(new VisualizerFrame(spectrum, new float[256], 0.6f, 0.4f), true, 12.5f, VisualizerMode.GpuBars);
        return state.CreateScene("GPU Bars");
    }

    public GpuVisualizerTests() => GpuVisualizers.Source = null;

    public void Dispose() => GpuVisualizers.Source = null;

    [Fact]
    public void A_gpu_frame_is_blitted_scaled_into_the_drawing_area()
    {
        var source = new FakeSource();
        GpuVisualizers.Source = source;
        var fallback = new CountingRenderer();
        var canvas = new NullVizCanvas();
        var bounds = new VizRect(10, 20, 640, 360);

        new GpuVisualizerRenderer(2, fallback).Draw(canvas, bounds, Scene());

        Assert.Equal(1, canvas.PixelBlits);
        Assert.Equal(0, fallback.Draws);
        var blit = canvas.LastBlit!.Value;
        Assert.Equal((640, 360), (blit.Width, blit.Height));
        Assert.Equal(bounds, blit.Dest);
        Assert.Equal(640 * 360 * 4, blit.Bgra.Length);
    }

    [Fact]
    public void The_scene_reaches_the_gpu_unchanged()
    {
        var source = new FakeSource();
        GpuVisualizers.Source = source;
        var scene = Scene();

        new GpuVisualizerRenderer(1, new CountingRenderer()).Draw(new NullVizCanvas(), new VizRect(0, 0, 320, 180), scene);

        var call = source.Last!.Value;
        Assert.Equal(1, call.Builtin);
        Assert.Equal(scene.PlaybackTimeSeconds, call.Time);
        Assert.Equal(scene.SpectrumLevels, call.Spectrum);
        Assert.Equal(scene.RmsLevel, call.Rms);
        Assert.Equal(scene.PeakLevel, call.Peak);
        Assert.Equal(scene.Theme.BarStartColor, call.Accent);
    }

    [Fact]
    public void Large_areas_render_capped_and_keep_their_aspect_ratio()
    {
        var source = new FakeSource();
        GpuVisualizers.Source = source;

        new GpuVisualizerRenderer(0, new CountingRenderer()).Draw(new NullVizCanvas(), new VizRect(0, 0, 2560, 1440), Scene());

        var call = source.Last!.Value;
        Assert.Equal(GpuVisualizerRenderer.MaxRenderWidth, call.W);
        Assert.Equal(720, call.H);
    }

    [Theory]
    [InlineData(640, 360, 1280, 640, 360)]    // small enough: rendered 1:1
    [InlineData(3840, 2160, 1280, 1280, 720)] // capped, same aspect
    [InlineData(400, 1200, 1280, 400, 1200)]  // portrait is untouched
    [InlineData(0, 0, 1280, 2, 2)]            // nothing to draw into
    [InlineData(float.NaN, 100, 1280, 2, 2)]
    [InlineData(1, 1, 1280, 2, 2)]            // never below 2px
    public void Render_size_follows_the_area(float w, float h, int cap, int expectedW, int expectedH)
    {
        Assert.Equal((expectedW, expectedH), GpuVisualizerRenderer.ChooseRenderSize(w, h, cap));
    }

    [Fact]
    public void No_source_means_the_cpu_fallback_draws()
    {
        var fallback = new CountingRenderer();
        var canvas = new NullVizCanvas();

        new GpuVisualizerRenderer(0, fallback).Draw(canvas, new VizRect(0, 0, 100, 100), Scene());

        Assert.Equal(1, fallback.Draws);
        Assert.Equal(0, canvas.PixelBlits);
    }

    [Fact]
    public void An_unavailable_source_is_not_even_asked_to_render()
    {
        var source = new FakeSource { IsAvailable = false };
        GpuVisualizers.Source = source;
        var fallback = new CountingRenderer();

        new GpuVisualizerRenderer(0, fallback).Draw(new NullVizCanvas(), new VizRect(0, 0, 100, 100), Scene());

        Assert.Equal(0, source.Calls);
        Assert.Equal(1, fallback.Draws);
    }

    [Fact]
    public void A_dropped_or_malformed_frame_falls_back_instead_of_going_blank()
    {
        var source = new FakeSource { ReturnWrongSize = true };
        GpuVisualizers.Source = source;
        var fallback = new CountingRenderer();
        var canvas = new NullVizCanvas();

        new GpuVisualizerRenderer(0, fallback).Draw(canvas, new VizRect(0, 0, 100, 100), Scene());

        Assert.Equal(1, fallback.Draws);
        Assert.Equal(0, canvas.PixelBlits);
    }

    // ── catalog gating ────────────────────────────────────────────────────────

    [Fact]
    public void Gpu_modes_are_registered_after_the_legacy_stubs_so_saved_settings_keep_their_meaning()
    {
        // The enum is persisted, so the original values must not have shifted.
        Assert.Equal(0, (int)VisualizerMode.Spectrum);
        Assert.Equal(1, (int)VisualizerMode.MirrorSpectrum);
        Assert.True((int)VisualizerMode.GpuBars > (int)VisualizerMode.BlockGrid);
        Assert.Equal(3, Enum.GetValues<VisualizerMode>().Count(m => m is VisualizerMode.GpuBars or VisualizerMode.GpuRadial or VisualizerMode.GpuTunnel));
    }

    [Fact]
    public void Gpu_visualizers_are_hidden_from_pickers_without_a_gpu_source()
    {
        Assert.DoesNotContain(VisualizerCatalog.All, d => d.RequiresGpu);
        Assert.Contains(VisualizerCatalog.AllRegistered, d => d.Mode == VisualizerMode.GpuBars);
    }

    [Fact]
    public void Gpu_visualizers_appear_once_a_source_is_available()
    {
        GpuVisualizers.Source = new FakeSource();

        var gpu = VisualizerCatalog.All.Where(d => d.RequiresGpu).Select(d => d.Mode).ToList();

        Assert.Equal([VisualizerMode.GpuBars, VisualizerMode.GpuRadial, VisualizerMode.GpuTunnel], gpu);
    }

    [Fact]
    public void A_source_that_stops_being_available_hides_them_again()
    {
        var source = new FakeSource();
        GpuVisualizers.Source = source;
        Assert.Contains(VisualizerCatalog.All, d => d.RequiresGpu);

        source.IsAvailable = false;

        Assert.DoesNotContain(VisualizerCatalog.All, d => d.RequiresGpu);
    }

    [Fact]
    public void A_saved_gpu_mode_still_resolves_and_renders_without_a_gpu()
    {
        var definition = VisualizerCatalog.GetDefinition(VisualizerMode.GpuRadial);
        var canvas = new NullVizCanvas();

        definition.Renderer.Draw(canvas, new VizRect(0, 0, 400, 300), Scene());

        Assert.Equal(VisualizerMode.GpuRadial, definition.Mode);
        Assert.True(canvas.CallCount > 0, "the CPU look-alike must draw something");
        Assert.Equal(0, canvas.PixelBlits);
    }

    [Fact]
    public void Gpu_entries_use_the_builtin_indexes_the_native_host_defines()
    {
        var source = new FakeSource();
        GpuVisualizers.Source = source;

        foreach (var (mode, index) in new[] { (VisualizerMode.GpuBars, 0), (VisualizerMode.GpuRadial, 1), (VisualizerMode.GpuTunnel, 2) })
        {
            VisualizerCatalog.GetDefinition(mode).Renderer.Draw(new NullVizCanvas(), new VizRect(0, 0, 200, 100), Scene());
            Assert.Equal(index, source.Last!.Value.Builtin);
        }
    }
}
