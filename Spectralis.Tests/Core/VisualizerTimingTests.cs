using Spectralis.Core.Visualizers;
using Xunit;

namespace Spectralis.Tests.Core;

public class VisualizerTimingTests
{
    private static readonly VisualizerFrame Signal = new(Enumerable.Repeat(.8f, 64).ToArray(), new float[256], .8f, .5f);

    [Theory]
    [InlineData(30)]
    [InlineData(60)]
    [InlineData(120)]
    public void AnimationSpeedDoesNotDependOnDisplayRefreshRate(int fps)
    {
        var scene = new VisualizerSceneState();
        for (var i = 0; i < fps; i++) scene.UpdateFrame(Signal, true, 0, VisualizerMode.SpinningDisk, 1f / fps);
        Assert.InRange(scene.DiskAngle, 22.79f, 22.81f);
    }

    [Theory]
    [InlineData(30)]
    [InlineData(60)]
    [InlineData(120)]
    public void AttacksEaseIntoNewAudioRatherThanSnappingToEveryFftUpdate(int fps)
    {
        var state = new VisualizerSceneState();
        state.UpdateFrame(Signal, true, 0, VisualizerMode.Spectrum, 1f / fps);
        Assert.InRange(state.CreateScene("Spectrum").SpectrumLevels[0], 0.001f, .7999f);
        for (var i = 1; i < fps; i++) state.UpdateFrame(Signal, true, 0, VisualizerMode.Spectrum, 1f / fps);
        Assert.InRange(state.CreateScene("Spectrum").SpectrumLevels[0], .799f, .801f);
    }
}
