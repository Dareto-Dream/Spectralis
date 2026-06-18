using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Spectralis.App.Services;

namespace Spectralis.App.ViewModels
{
    public partial class OBSOverlayViewModel : ObservableObject
    {
        private readonly OBSOverlayServer _server;

        [ObservableProperty] private bool _isRunning;
        [ObservableProperty] private int _port = OBSOverlayServer.DefaultPort;
        [ObservableProperty] private string _overlayUrl = $"http://localhost:{OBSOverlayServer.DefaultPort}/overlay";
        [ObservableProperty] private string _statusText = "Server stopped";

        public OBSOverlayViewModel(OBSOverlayServer server)
        {
            _server = server;
        }

        [RelayCommand]
        private void ToggleServer()
        {
            if (IsRunning)
            {
                _server.Stop();
                IsRunning = false;
                StatusText = "Server stopped";
            }
            else
            {
                _server.Start();
                IsRunning = true;
                StatusText = $"Running at http://localhost:{Port}";
                OverlayUrl = $"http://localhost:{Port}/overlay";
            }
        }

        [RelayCommand]
        private void CopyUrl()
        {
        }
    }
}
