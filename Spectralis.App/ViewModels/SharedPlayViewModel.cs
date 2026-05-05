using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Spectralis.App.Services;
using Spectralis.Core.SharedPlay;

namespace Spectralis.App.ViewModels
{
    public partial class SharedPlayViewModel : ObservableObject
    {
        private readonly SharedPlayService _service;

        [ObservableProperty] private string _roomCode = string.Empty;
        [ObservableProperty] private string _userId = string.Empty;
        [ObservableProperty] private bool _isConnected;
        [ObservableProperty] private bool _isHost;
        [ObservableProperty] private string _statusText = "Disconnected";
        [ObservableProperty] private int _listenerCount;
        [ObservableProperty] private SharedPlaySyncPacket? _lastSync;

        public SharedPlayViewModel(SharedPlayService service)
        {
            _service = service;
            _service.RemoteSyncReceived += OnRemoteSync;
            _service.SessionEnded += OnSessionEnded;
        }

        [RelayCommand]
        private async Task HostAsync()
        {
            if (string.IsNullOrWhiteSpace(RoomCode)) return;
            try
            {
                await _service.HostAsync(RoomCode, UserId, () => TimeSpan.Zero);
                IsConnected = true;
                IsHost = true;
                StatusText = $"Hosting room {RoomCode}";
            }
            catch (Exception ex) { StatusText = $"Error: {ex.Message}"; }
        }

        [RelayCommand]
        private async Task JoinAsync()
        {
            if (string.IsNullOrWhiteSpace(RoomCode)) return;
            try
            {
                await _service.JoinAsync(RoomCode, UserId);
                IsConnected = true;
                IsHost = false;
                StatusText = $"Joined room {RoomCode}";
            }
            catch (Exception ex) { StatusText = $"Error: {ex.Message}"; }
        }

        [RelayCommand]
        private void Leave()
        {
            _service.Dispose();
            IsConnected = false;
            IsHost = false;
            StatusText = "Disconnected";
        }

        private void OnRemoteSync(object? sender, SharedPlaySyncPacket packet)
        {
            LastSync = packet;
            StatusText = $"Synced at {TimeSpan.FromSeconds(packet.PositionSeconds):mm\\:ss}";
        }

        private void OnSessionEnded(object? sender, EventArgs e)
        {
            IsConnected = false;
            StatusText = "Session ended by host";
        }
    }
}
