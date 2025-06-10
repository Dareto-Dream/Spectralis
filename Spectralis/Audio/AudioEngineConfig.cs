namespace Spectralis.Audio
{
    public class AudioEngineConfig
    {
        public int BufferMilliseconds { get; set; } = 200;
        public bool PreferWasapi { get; set; } = true;
        public bool WasapiExclusive { get; set; } = false;
        public string PreferredDeviceId { get; set; } = null;
        public float InitialVolume { get; set; } = 0.8f;
        public bool NormalizeVolumeOnSwitch { get; set; } = false;

        public static AudioEngineConfig Default => new AudioEngineConfig();
    }
}
