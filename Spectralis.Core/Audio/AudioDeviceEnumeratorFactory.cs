using Spectralis.Core.Platform;

namespace Spectralis.Core.Audio;

/// <summary>Picks the platform's audio-output backend: WaveOut (winmm) on
/// Windows, PulseAudio's `paplay` subprocess on Linux, the bundled ffmpeg's
/// audiotoolbox device on macOS — the same per-platform split <see cref="Platform.LoopbackCaptureSourceFactory"/>
/// uses for capture. macOS previously fell through to the WaveOut leg and threw
/// "Unable to load shared library 'winmm.dll'" the moment a device was created.</summary>
public static class AudioDeviceEnumeratorFactory
{
    public static IAudioDeviceEnumerator Create()
    {
        if (OperatingSystem.IsLinux())
        {
            return new PulseAudioDeviceEnumerator();
        }

        if (OperatingSystem.IsMacOS())
        {
            return new AudioToolboxDeviceEnumerator();
        }

        return new WaveOutDeviceEnumerator();
    }
}
