using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Spectralis.App.Services;

namespace Spectralis.App.ViewModels
{
    public partial class ScrobblingStatusViewModel : ObservableObject
    {
        private readonly ScrobbleService _service;

        [ObservableProperty] private bool _isEnabled;
        [ObservableProperty] private string _lastFmStatus = "Not connected";
        [ObservableProperty] private string _listenBrainzStatus = "Not connected";
        [ObservableProperty] private int _pendingCount;
        [ObservableProperty] private string _currentTrackScrobbleStatus = string.Empty;

        public ScrobblingStatusViewModel(ScrobbleService service)
        {
            _service = service;
            _service.StatusChanged += OnStatusChanged;
            RefreshStatus();
        }

        private void OnStatusChanged(object? sender, ScrobbleStatusEventArgs e)
        {
            LastFmStatus = e.LastFmConnected ? "Connected" : "Disconnected";
            ListenBrainzStatus = e.ListenBrainzConnected ? "Connected" : "Disconnected";
            PendingCount = e.PendingCount;
            CurrentTrackScrobbleStatus = e.CurrentStatus;
        }

        private void RefreshStatus()
        {
            PendingCount = _service.PendingQueueCount;
            IsEnabled = _service.IsEnabled;
        }

        [RelayCommand]
        private async System.Threading.Tasks.Task FlushPendingAsync()
        {
            await _service.FlushPendingAsync();
            RefreshStatus();
        }

        [RelayCommand]
        private void Toggle()
        {
            _service.IsEnabled = !_service.IsEnabled;
            IsEnabled = _service.IsEnabled;
        }
    }

    public class ScrobbleStatusEventArgs : System.EventArgs
    {
        public bool LastFmConnected { get; init; }
        public bool ListenBrainzConnected { get; init; }
        public int PendingCount { get; init; }
        public string CurrentStatus { get; init; } = string.Empty;
    }
}
