using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Spectralis.Core.Infrastructure;
using Spectralis.Core.Settings;

namespace Spectralis.App.ViewModels
{
    public partial class SettingsViewModel : ViewModelBase
    {
        private readonly SettingsRepository _repo;

        [ObservableProperty] private float _volume = 0.8f;
        [ObservableProperty] private bool _minimizeToTray = true;
        [ObservableProperty] private bool _scanOnStartup;
        [ObservableProperty] private bool _scrobblingEnabled;
        [ObservableProperty] private string _lastFmUsername = string.Empty;
        [ObservableProperty] private bool _replayGainEnabled;
        [ObservableProperty] private string _spotifyClientId = string.Empty;
        [ObservableProperty] private string _youTubeApiKey = string.Empty;
        [ObservableProperty] private string _soundCloudClientId = string.Empty;
        [ObservableProperty] private string _ytDlpPath = string.Empty;
        [ObservableProperty] private int _coverArtCacheLimitMb = 512;
        [ObservableProperty] private string _statusMessage = string.Empty;

        public SettingsViewModel(SettingsRepository repo)
        {
            _repo = repo;
        }

        [RelayCommand]
        private async Task LoadAsync()
        {
            var s = await _repo.LoadAsync();
            Volume = s.Volume;
            MinimizeToTray = s.MinimizeToTray;
            ScanOnStartup = s.ScanOnStartup;
            ScrobblingEnabled = s.ScrobblingEnabled;
            LastFmUsername = s.LastFmUsername;
            ReplayGainEnabled = s.ReplayGainEnabled;
            SpotifyClientId = s.SpotifyClientId;
            YouTubeApiKey = s.YouTubeApiKey;
            SoundCloudClientId = s.SoundCloudClientId;
            YtDlpPath = s.YtDlpPath;
            CoverArtCacheLimitMb = s.CoverArtCacheLimitMb;
        }

        [RelayCommand]
        private async Task SaveAsync()
        {
            var s = new AppSettings
            {
                Volume = Volume,
                MinimizeToTray = MinimizeToTray,
                ScanOnStartup = ScanOnStartup,
                ScrobblingEnabled = ScrobblingEnabled,
                LastFmUsername = LastFmUsername,
                ReplayGainEnabled = ReplayGainEnabled,
                SpotifyClientId = SpotifyClientId,
                YouTubeApiKey = YouTubeApiKey,
                SoundCloudClientId = SoundCloudClientId,
                YtDlpPath = YtDlpPath,
                CoverArtCacheLimitMb = CoverArtCacheLimitMb
            };
            await _repo.SaveAsync(s);
            StatusMessage = "Settings saved.";
        }
    }
}
