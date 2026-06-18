namespace Spectralis.Core.Settings
{
    public class BroadcastSettings
    {
        public bool DiscordRpcEnabled { get; set; } = true;
        public string DiscordClientId { get; set; } = "1234567890";
        public bool OBSOverlayEnabled { get; set; } = false;
        public int OBSOverlayPort { get; set; } = 5128;
        public bool SharedPlayEnabled { get; set; } = false;
        public string SharedPlayServerUri { get; set; } = "wss://play.spectralis.app";
    }
}
