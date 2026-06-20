using Spectralis.Core.Audio.Loopback;
using Spectralis.Core.Visualizers;

namespace Spectralis.Core.Platform;

/// <summary>
/// Captures what the system (or a target process) is playing and feeds it into
/// a visualizer — the seam behind the Spotify loopback visualizer. Per-platform
/// backends: WASAPI loopback on Windows, PulseAudio/PipeWire monitor source on
/// Linux, AVAudioEngine/BlackHole on macOS.
/// </summary>
public interface ILoopbackCaptureSource : IDisposable
{
    /// <summary>Whether this backend can capture on the current machine.</summary>
    bool IsSupported { get; }

    /// <summary>Human-readable capture state ("system-loopback", failure detail, setup hint).</summary>
    string StatusDetail { get; }

    /// <summary>Begins capture into the visualizer. Returns false (with detail) on failure.</summary>
    bool Start(VisualizerSampleProvider target, int? targetProcessId = null);

    void Stop();
}

public static class LoopbackCaptureSourceFactory
{
    public static ILoopbackCaptureSource Create()
    {
        if (OperatingSystem.IsWindows())
        {
            return new WindowsLoopbackCaptureSource();
        }

        if (OperatingSystem.IsLinux())
        {
            return new PulseAudioLoopbackCaptureSource();
        }

        if (OperatingSystem.IsMacOS())
        {
            return new MacLoopbackCaptureSource();
        }

        return new UnsupportedLoopbackCaptureSource();
    }
}

/// <summary>Fallback for platforms without a capture backend.</summary>
public sealed class UnsupportedLoopbackCaptureSource : ILoopbackCaptureSource
{
    public bool IsSupported => false;
    public string StatusDetail => "Loopback capture is not supported on this platform.";
    public bool Start(VisualizerSampleProvider target, int? targetProcessId = null) => false;
    public void Stop() { }
    public void Dispose() { }
}
