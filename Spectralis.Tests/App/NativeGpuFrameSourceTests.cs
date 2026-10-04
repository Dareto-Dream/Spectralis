using Spectralis.App.Gpu;
using Spectralis.Core.Visualizers;
using Xunit;

namespace Spectralis.Tests.App;

/// <summary>
/// Runs the real native wgpu visualizer pipeline. Like the world renderer tests, these skip (not fail) when
/// the native library or a compatible GPU adapter isn't on the machine.
/// </summary>
// Same collection as the sustained-frame-time benchmark so real GPU work never overlaps its timing.
[Collection("GpuVisualizerSource")]
public sealed class NativeGpuFrameSourceTests : IDisposable
{
    private readonly NativeGpuFrameSource _source = new();

    public void Dispose() => _source.Dispose();

    private static readonly float[] Spectrum = Enumerable.Range(0, 64).Select(i => 1f - (i / 80f)).ToArray();
    private static readonly VizColor Orange = VizColor.FromRgb(255, 80, 26);

    /// <summary>A first frame, or null when this machine can't do GPU visualizers (the test then returns early).</summary>
    private byte[]? FirstFrame(int builtin, int w = 96, int h = 64)
    {
        var frame = _source.RenderFrame(builtin, w, h, 1.0, Spectrum, 0.5f, 0.9f, Orange);
        if (frame is null) Console.Error.WriteLine("skipping: no GPU visualizer support on this machine");
        return frame;
    }

    private static int Lit(byte[] bgra) =>
        Enumerable.Range(0, bgra.Length / 4).Count(i => bgra[i * 4] + bgra[(i * 4) + 1] + bgra[(i * 4) + 2] > 120);

    [Fact]
    public void Every_builtin_draws_a_bgra_frame_of_the_requested_size()
    {
        if (FirstFrame(0) is null) return;

        for (var builtin = 0; builtin < 3; builtin++)
        {
            var frame = _source.RenderFrame(builtin, 96, 64, 2.0, Spectrum, 0.5f, 0.9f, Orange);

            Assert.NotNull(frame);
            Assert.Equal(96 * 64 * 4, frame!.Length);
            Assert.True(Lit(frame) > 20, $"built-in {builtin} drew an almost black frame");
        }
    }

    [Fact]
    public void Colour_channels_are_in_bgra_order_for_the_canvas()
    {
        if (FirstFrame(0) is null) return;

        // Loud bars in an orange accent: lit pixels should be red-dominant, which in BGRA means R (index 2) > B (index 0).
        var frame = _source.RenderFrame(0, 96, 64, 1.0, Enumerable.Repeat(1f, 64).ToArray(), 0.8f, 1f, Orange)!;
        var lit = Enumerable.Range(0, frame.Length / 4).Select(i => (b: frame[i * 4], r: frame[(i * 4) + 2])).Where(p => p.r > 150).ToList();

        Assert.NotEmpty(lit);
        Assert.True(lit.Average(p => p.r) > lit.Average(p => p.b) + 60, "red should dominate an orange visualizer in BGRA");
    }

    [Fact]
    public void Louder_audio_changes_the_picture()
    {
        if (FirstFrame(0) is null) return;

        var quiet = _source.RenderFrame(0, 96, 64, 1.0, new float[64], 0f, 0f, Orange)!.ToArray();
        var loud = _source.RenderFrame(0, 96, 64, 1.0, Enumerable.Repeat(1f, 64).ToArray(), 0.8f, 1f, Orange)!.ToArray();

        Assert.NotEqual(quiet, loud);
        Assert.True(Lit(loud) > Lit(quiet) + 20);
    }

    [Fact]
    public void Resizing_rebuilds_the_renderer_and_keeps_rendering()
    {
        if (FirstFrame(0, 64, 48) is null) return;

        var bigger = _source.RenderFrame(0, 200, 120, 1.0, Spectrum, 0.5f, 0.9f, Orange);
        var back = _source.RenderFrame(1, 64, 48, 1.0, Spectrum, 0.5f, 0.9f, Orange);

        Assert.Equal(200 * 120 * 4, bigger!.Length);
        Assert.Equal(64 * 48 * 4, back!.Length);
    }

    [Fact]
    public void Switching_builtins_between_frames_changes_the_visualizer()
    {
        if (FirstFrame(0) is null) return;

        var bars = _source.RenderFrame(0, 96, 64, 1.0, Spectrum, 0.5f, 0.9f, Orange)!.ToArray();
        var radial = _source.RenderFrame(1, 96, 64, 1.0, Spectrum, 0.5f, 0.9f, Orange)!.ToArray();

        Assert.NotEqual(bars, radial);
    }

    [Fact]
    public void An_out_of_range_builtin_drops_the_frame_instead_of_throwing()
    {
        if (FirstFrame(0) is null) return;

        Assert.Null(_source.RenderFrame(99, 96, 64, 1.0, Spectrum, 0.5f, 0.9f, Orange));
        // ...and the source recovers on the next valid request.
        Assert.NotNull(_source.RenderFrame(0, 96, 64, 1.0, Spectrum, 0.5f, 0.9f, Orange));
    }

    [Fact]
    public void Hostile_audio_values_do_not_break_the_frame()
    {
        if (FirstFrame(0) is null) return;

        var frame = _source.RenderFrame(1, 96, 64, float.NaN, [float.NaN, float.PositiveInfinity, -3f], float.NaN, float.PositiveInfinity, Orange);

        Assert.NotNull(frame);
        Assert.Equal(96 * 64 * 4, frame!.Length);
    }

    [Fact]
    public void Native_builtin_count_matches_the_catalogs_gpu_entries()
    {
        if (FirstFrame(0) is null) return;

        var gpuEntries = VisualizerCatalog.AllRegistered.Count(d => d.RequiresGpu);

        Assert.Equal(gpuEntries, NativeGpuFrameSource.NativeBuiltinCount);
    }
}
