using Spectralis.Core.Platform;

namespace Spectralis.Core.Audio;

/// <summary>Picks the platform's audio-output backend: WaveOut (winmm) on
/// Windows, PulseAudio's `paplay` subprocess on Linux — the same per-platform
/// split <see cref="Platform.LoopbackCaptureSourceFactory"/> uses for capture.</summary>
public static class AudioDeviceEnumeratorFactory
{
    public static IAudioDeviceEnumerator Create() =>
        OperatingSystem.IsLinux()
            ? new PulseAudioDeviceEnumerator()
            : new WaveOutDeviceEnumerator();
}
