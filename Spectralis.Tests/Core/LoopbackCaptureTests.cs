using Spectralis.Core.Audio.Loopback;
using Spectralis.Core.Platform;
using Spectralis.Core.Visualizers;
using NAudio.Wave;
using Xunit;

namespace Spectralis.Tests.Core;

public class LoopbackCaptureFactoryTests
{
    [Fact]
    public void Factory_ReturnsPlatformBackend()
    {
        using var source = LoopbackCaptureSourceFactory.Create();

        if (OperatingSystem.IsWindows())
        {
            Assert.IsType<WindowsLoopbackCaptureSource>(source);
            Assert.True(source.IsSupported);
        }
        else if (OperatingSystem.IsLinux())
        {
            Assert.IsType<PulseAudioLoopbackCaptureSource>(source);
        }
        else if (OperatingSystem.IsMacOS())
        {
            Assert.IsType<MacLoopbackCaptureSource>(source);
        }
    }

    [Fact]
    public void Unsupported_ReportsCleanly()
    {
        using var source = new UnsupportedLoopbackCaptureSource();
        Assert.False(source.IsSupported);
        Assert.False(source.Start(MakeVisualizer()));
        source.Stop(); // no-throw
    }

    internal static VisualizerSampleProvider MakeVisualizer()
    {
        var silence = new SilenceProvider(WaveFormat.CreateIeeeFloatWaveFormat(44100, 2));
        return new VisualizerSampleProvider(silence.ToSampleProvider());
    }
}

public class PulseAudioArgumentTests
{
    [Fact]
    public void MonitorSourceName_AppendsMonitorSuffix()
    {
        Assert.Equal(
            "alsa_output.pci-0000_00_1f.3.analog-stereo.monitor",
            PulseAudioLoopbackCaptureSource.BuildMonitorSourceName(" alsa_output.pci-0000_00_1f.3.analog-stereo \n"));
    }

    [Fact]
    public void ParecArguments_AreDiscreteTokens()
    {
        var args = PulseAudioLoopbackCaptureSource.BuildParecArguments("sink.monitor");

        Assert.Contains("--format=s16le", args);
        Assert.Contains("--rate=44100", args);
        Assert.Contains("--channels=2", args);
        var deviceFlag = Array.IndexOf(args, "-d");
        Assert.True(deviceFlag >= 0);
        Assert.Equal("sink.monitor", args[deviceFlag + 1]);
    }

    [Fact]
    public void DefaultSinkQuery_UsesGetDefaultSink()
    {
        Assert.Equal(new[] { "get-default-sink" }, PulseAudioLoopbackCaptureSource.BuildDefaultSinkQueryArguments());
    }
}

public class MacLoopbackTests
{
    [Fact]
    public void Stub_ReportsSetupPathWithoutThrowing()
    {
        using var source = new MacLoopbackCaptureSource();

        var started = source.Start(LoopbackCaptureFactoryTests.MakeVisualizer());

        Assert.False(started);
        Assert.StartsWith("setup-required", source.StatusDetail);
        Assert.Contains("BlackHole", MacLoopbackCaptureSource.SetupInstructions + MacLoopbackCaptureSource.BlackHoleDownloadUrl);
    }
}

public class WindowsLoopbackTests
{
    [Fact]
    public void StartStopDispose_AreSafeAndReportStatus()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using var source = new WindowsLoopbackCaptureSource();
        var visualizer = LoopbackCaptureFactoryTests.MakeVisualizer();

        // Hardware-dependent: a headless box may have no render endpoint, so the
        // contract under test is "never throws, always explains itself".
        var started = source.Start(visualizer);
        Assert.False(string.IsNullOrWhiteSpace(source.StatusDetail));
        Assert.NotEqual("not-started", source.StatusDetail);

        if (started)
        {
            Assert.Contains("loopback", source.StatusDetail);
        }

        source.Stop();
        source.Stop(); // idempotent
    }
}
