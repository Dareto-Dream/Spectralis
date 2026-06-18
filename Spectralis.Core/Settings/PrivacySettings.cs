namespace Spectralis.Core.Settings
{
    public class PrivacySettings
    {
        public bool ShareNowPlaying { get; set; } = true;
        public bool AllowListeningHistory { get; set; } = true;
        public bool AllowScrobbling { get; set; } = true;
        public bool ShowInDiscord { get; set; } = true;
        public bool AllowSharedPlay { get; set; } = true;
        public bool AnonymousSharedPlay { get; set; } = false;
    }
}
