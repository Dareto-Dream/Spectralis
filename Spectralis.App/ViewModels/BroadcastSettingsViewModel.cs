using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Spectralis.Core.Settings;

namespace Spectralis.App.ViewModels
{
    public partial class BroadcastSettingsViewModel : ObservableObject
    {
        [ObservableProperty] private bool _discordRpcEnabled = true;
        [ObservableProperty] private string _discordClientId = "1234567890";
        [ObservableProperty] private bool _obsOverlayEnabled;
        [ObservableProperty] private int _obsOverlayPort = 5128;
        [ObservableProperty] private bool _sharedPlayEnabled;
        [ObservableProperty] private string _sharedPlayServerUri = "wss://play.spectralis.app";

        public void LoadFrom(BroadcastSettings settings)
        {
            DiscordRpcEnabled = settings.DiscordRpcEnabled;
            DiscordClientId = settings.DiscordClientId;
            OBSOverlayEnabled = settings.OBSOverlayEnabled;
            OBSOverlayPort = settings.OBSOverlayPort;
            SharedPlayEnabled = settings.SharedPlayEnabled;
            SharedPlayServerUri = settings.SharedPlayServerUri;
        }

        public BroadcastSettings ToSettings() => new()
        {
            DiscordRpcEnabled = DiscordRpcEnabled,
            DiscordClientId = DiscordClientId,
            OBSOverlayEnabled = OBSOverlayEnabled,
            OBSOverlayPort = OBSOverlayPort,
            SharedPlayEnabled = SharedPlayEnabled,
            SharedPlayServerUri = SharedPlayServerUri
        };

        [RelayCommand]
        private void ResetDefaults()
        {
            var defaults = new BroadcastSettings();
            LoadFrom(defaults);
        }
    }
}
