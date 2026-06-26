using Spectralis.Core.Platform;
using Spectralis.Core.Visualizers;

namespace Spectralis.Core.Audio.Loopback;

/// <summary>
/// macOS backend per the BLOCKERS decision: AVAudioEngine input tap on
/// macOS 14.2+, BlackHole virtual-device guidance on older versions. The
/// AVAudioEngine binding stays stubbed until smoke-tested on a real Mac
/// (see BLOCKERS.md); until then Start reports the setup path.
/// </summary>
public sealed class MacLoopbackCaptureSource : ILoopbackCaptureSource
{
    public const string BlackHoleDownloadUrl = "https://existential.audio/blackhole/";

    public static string SetupInstructions =>
        OperatingSystem.IsMacOSVersionAtLeast(14, 2)
            ? "System audio capture uses the macOS audio tap; grant Spectralis the Screen & System Audio Recording permission in System Settings → Privacy."
            : $"This macOS version needs the free BlackHole virtual audio device. Install it from {BlackHoleDownloadUrl}, set a Multi-Output Device with BlackHole, then restart Spectralis.";

    public bool IsSupported => OperatingSystem.IsMacOS();

    public string StatusDetail { get; private set; } = "not-started";

    public bool Start(VisualizerSampleProvider target, int? targetProcessId = null)
    {
        // Pending real-Mac validation of the AVAudioEngine/MediaPlayer bindings.
        StatusDetail = "setup-required: " + SetupInstructions;
        return false;
    }

    public void Stop()
    {
    }

    public void Dispose()
    {
    }
}
