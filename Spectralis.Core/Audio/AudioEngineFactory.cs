using System.Runtime.InteropServices;

namespace Spectralis.Core.Audio
{
    public static class AudioEngineFactory
    {
        public static IAudioEngine Create(AudioEngineOptions? options = null)
        {
            return new NaudioAudioEngine(options);
        }

        public static IAudioCapture? CreateLoopbackCapture()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                return new WasapiLoopbackCaptureAdapter();
            return null;
        }
    }
}
