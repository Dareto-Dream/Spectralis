using System.Diagnostics;
using System.Numerics;
using Spectralis.Core.Visualizers;
using Xunit;

namespace Spectralis.Tests.Core;

public class VisualizerSceneStateTests
{
    private static VisualizerFrame MakeFrame(float level = 0.5f)
    {
        var spectrum = new float[64];
        var waveform = new float[256];
        Array.Fill(spectrum, level);
        for (var i = 0; i < waveform.Length; i++)
        {
            waveform[i] = level * MathF.Sin(i * 0.1f);
        }

        return new VisualizerFrame(spectrum, waveform, level, level * 0.7f);
    }

    [Fact]
    public void UpdateFrame_AppliesSensitivityAndClamp()
    {
        var state = new VisualizerSceneState { Sensitivity = 2.5f };
        state.UpdateFrame(MakeFrame(1.0f), activePlayback: true, 0, VisualizerMode.Spectrum);

        var scene = state.CreateScene("test");
        Assert.All(scene.SpectrumLevels, level => Assert.InRange(level, 0, 1.25f));
    }

    [Fact]
    public void SpectrumDecays_WhenSignalDrops()
    {
        var state = new VisualizerSceneState();
        state.UpdateFrame(MakeFrame(0.8f), true, 0, VisualizerMode.Spectrum);
        var loud = state.CreateScene("t").SpectrumLevels[10];

        state.UpdateFrame(MakeFrame(0f), true, 0.1f, VisualizerMode.Spectrum);
        var decayed = state.CreateScene("t").SpectrumLevels[10];

        Assert.True(decayed < loud);
        Assert.InRange(decayed, loud * 0.79f, loud * 0.81f); // 0.80 decay factor
    }

    [Fact]
    public void PeakHold_FallsSlowerThanSpectrum()
    {
        var state = new VisualizerSceneState();
        state.UpdateFrame(MakeFrame(0.8f), true, 0, VisualizerMode.Spectrum);

        for (var i = 0; i < 5; i++)
        {
            state.UpdateFrame(MakeFrame(0f), true, 0.1f * i, VisualizerMode.Spectrum);
        }

        var scene = state.CreateScene("t");
        Assert.True(scene.PeakHoldLevels[10] > scene.SpectrumLevels[10]);
    }

    [Fact]
    public void PeakHold_DisabledWhenShowPeaksOff()
    {
        var state = new VisualizerSceneState { ShowPeaks = false };
        state.UpdateFrame(MakeFrame(0.8f), true, 0, VisualizerMode.Spectrum);

        Assert.All(state.CreateScene("t").PeakHoldLevels, level => Assert.Equal(0, level));
    }

    [Fact]
    public void DiskAngle_AdvancesOnlyForSpinningDiskWhilePlaying()
    {
        var state = new VisualizerSceneState();

        state.UpdateFrame(MakeFrame(), true, 0, VisualizerMode.Spectrum);
        Assert.Equal(0, state.DiskAngle);

        state.UpdateFrame(MakeFrame(), true, 0, VisualizerMode.SpinningDisk);
        Assert.True(state.DiskAngle > 0);

        var angle = state.DiskAngle;
        state.UpdateFrame(MakeFrame(), false, 0, VisualizerMode.SpinningDisk);
        Assert.Equal(angle, state.DiskAngle);
    }

    [Fact]
    public void AnimationPhase_AdvancesForAnimatedModes()
    {
        var state = new VisualizerSceneState();
        state.UpdateFrame(MakeFrame(), true, 0, VisualizerMode.RadialSpectrum);
        Assert.True(state.AnimationPhase > 0);
    }

    [Fact]
    public void Clear_ResetsEverything()
    {
        var state = new VisualizerSceneState();
        state.UpdateFrame(MakeFrame(0.9f), true, 5, VisualizerMode.SpinningDisk);
        state.Clear();

        var scene = state.CreateScene("t");
        Assert.All(scene.SpectrumLevels, level => Assert.Equal(0, level));
        Assert.Equal(0, scene.PeakLevel);
        Assert.Equal(0, state.DiskAngle);
        Assert.False(scene.IsActive);
    }
}

public class VizMathTests
{
    [Fact]
    public void CardinalSpline_PassesThroughControlPoints()
    {
        var points = new[] { new Vector2(0, 0), new Vector2(10, 5), new Vector2(20, 0) };
        var curve = VizMath.CardinalSpline(points, 0.4f, 8);

        Assert.Equal(points[0], curve[0]);
        Assert.Equal(points[^1], curve[^1]);
        Assert.Contains(curve, p => Vector2.Distance(p, points[1]) < 0.001f);
        Assert.Equal((points.Length - 1) * 8 + 1, curve.Length);
    }

    [Fact]
    public void CardinalSpline_HandlesDegenerateInput()
    {
        Assert.Empty(VizMath.CardinalSpline(Array.Empty<Vector2>(), 0.4f));
        Assert.Single(VizMath.CardinalSpline(new[] { new Vector2(1, 1) }, 0.4f));
    }

    [Fact]
    public void SampleRange_AveragesBuckets()
    {
        var source = new float[] { 1f, 1f, 0f, 0f };
        Assert.Equal(1f, VizMath.SampleRange(source, 0, 2));
        Assert.Equal(0f, VizMath.SampleRange(source, 1, 2));
    }

    [Fact]
    public void SampleRange_EmptySourceReturnsZero()
    {
        Assert.Equal(0f, VizMath.SampleRange(Array.Empty<float>(), 0, 8));
    }
}

public class VisualizerRendererTests
{
    public static TheoryData<VisualizerMode> AllModes()
    {
        var data = new TheoryData<VisualizerMode>();
        foreach (var definition in VisualizerCatalog.All)
        {
            data.Add(definition.Mode);
        }

        return data;
    }

    private static VisualizerScene MakeScene()
    {
        var state = new VisualizerSceneState();
        var spectrum = new float[64];
        var waveform = new float[256];
        var random = new Random(42);
        for (var i = 0; i < spectrum.Length; i++)
        {
            spectrum[i] = (float)random.NextDouble();
        }

        for (var i = 0; i < waveform.Length; i++)
        {
            waveform[i] = ((float)random.NextDouble() * 2f) - 1f;
        }

        state.UpdateFrame(new VisualizerFrame(spectrum, waveform, 0.8f, 0.5f), true, 42.5f, VisualizerMode.Spectrum);
        return state.CreateScene("bench");
    }

    [Theory]
    [MemberData(nameof(AllModes))]
    public void Renderer_DrawsWithoutThrowing(VisualizerMode mode)
    {
        var definition = VisualizerCatalog.GetDefinition(mode);
        var canvas = new NullVizCanvas();

        definition.Renderer.Draw(canvas, new VizRect(0, 0, 1280, 720), MakeScene());

        Assert.True(canvas.CallCount > 0);
    }

    [Theory]
    [MemberData(nameof(AllModes))]
    public void Renderer_FrameLogicStaysWithinBudget(VisualizerMode mode)
    {
        var definition = VisualizerCatalog.GetDefinition(mode);
        var canvas = new NullVizCanvas();
        var scene = MakeScene();
        var bounds = new VizRect(0, 0, 1920, 1080);

        // Warmup (JIT)
        for (var i = 0; i < 10; i++)
        {
            definition.Renderer.Draw(canvas, bounds, scene);
        }

        const int frames = 120;
        var sw = Stopwatch.StartNew();
        for (var i = 0; i < frames; i++)
        {
            definition.Renderer.Draw(canvas, bounds, scene);
        }

        sw.Stop();

        // Renderer math must leave the bulk of the 16.7ms frame budget for the
        // actual GPU/draw work. 4ms average is a generous ceiling on CI boxes.
        var averageMs = sw.Elapsed.TotalMilliseconds / frames;
        Assert.True(averageMs < 4.0, $"{mode} averaged {averageMs:0.00}ms of frame logic per frame.");
    }

    [Fact]
    public void Catalog_RegistersAllImplementedVisualizers()
    {
        Assert.Equal(16, VisualizerCatalog.All.Count);

        // Legacy stub modes intentionally ship without renderers, mirroring WinForms.
        VisualizerMode[] stubs =
        [
            VisualizerMode.LedMeter,
            VisualizerMode.Vectorscope,
            VisualizerMode.BounceBars,
            VisualizerMode.CircularEq,
            VisualizerMode.BlockGrid,
        ];
        Assert.All(
            Enum.GetValues<VisualizerMode>().Except(stubs),
            mode => Assert.Contains(VisualizerCatalog.All, definition => definition.Mode == mode));
        Assert.All(
            stubs,
            mode => Assert.DoesNotContain(VisualizerCatalog.All, definition => definition.Mode == mode));
    }

    [Fact]
    public void Renderer_HandlesZeroSizeBounds()
    {
        foreach (var definition in VisualizerCatalog.All)
        {
            definition.Renderer.Draw(new NullVizCanvas(), new VizRect(0, 0, 0, 0), MakeScene());
        }
    }
}
