using NAudio.Wave.SampleProviders;
using Spectralis.Core.Visualizers;
using Xunit;

namespace Spectralis.Tests.Core;

public class VisualizerSnapshotTests
{
    [Fact]
    public void SpectrogramFramesShareImmutableRowsAndSurviveRingUpdatesAndClear()
    {
        var provider = new VisualizerSampleProvider(new SignalGenerator(48000, 2) { Frequency = 440, Gain = .5 });
        var audio = new float[4096];
        for (var i = 0; i < 5; i++) provider.Read(audio, 0, audio.Length);
        var before = provider.GetFrame(includeSpectrogram: true);
        var expected = (float[])before.SpectrogramHistory[before.SpectrogramNewestIndex].Clone();
        var sameCycle = provider.GetFrame(includeSpectrogram: true);
        Assert.NotSame(before.SpectrogramHistory, sameCycle.SpectrogramHistory);
        Assert.Same(before.SpectrogramHistory[before.SpectrogramNewestIndex], sameCycle.SpectrogramHistory[sameCycle.SpectrogramNewestIndex]);
        provider.Read(audio, 0, audio.Length);
        provider.Clear();
        Assert.Equal(expected, before.SpectrogramHistory[before.SpectrogramNewestIndex]);
        Assert.All(provider.GetFrame(includeSpectrogram: true).SpectrogramHistory, row => Assert.All(row, value => Assert.Equal(0, value)));
    }

    [Fact]
    public void SpectrogramSnapshotsDoNotAllocateAWholeHistoryOnEveryFrame()
    {
        var provider = new VisualizerSampleProvider(new SignalGenerator(48000, 2));
        provider.GetFrame(includeSpectrogram: true);
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 60; i++) provider.GetFrame(includeSpectrogram: true);
        Assert.InRange(GC.GetAllocatedBytesForCurrentThread() - before, 0, 2_000_000);
    }
}
