using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Spectralis.App.Services;
using Spectralis.Core.Models;

namespace Spectralis.App.ViewModels
{
    public partial class MainViewModel : ViewModelBase
    {
        private readonly ServiceContainer _services;

        public PlayerViewModel Player { get; }
        public LibraryViewModel Library { get; }
        public QueueViewModel Queue { get; }
        public StreamingViewModel Streaming { get; }
        public BpmViewModel Bpm { get; }

        [ObservableProperty] private bool _isLibraryVisible = true;
        [ObservableProperty] private bool _isQueueVisible = true;
        [ObservableProperty] private string _statusMessage = "Ready";

        public MainViewModel(ServiceContainer services)
        {
            _services = services;
            Player = new PlayerViewModel(services);
            Library = new LibraryViewModel(services);
            Queue = new QueueViewModel(services);
            Streaming = new StreamingViewModel(services.Streaming, services.Queue, services.AudioEngine);
            Bpm = new BpmViewModel(services.Analysis, services.AnalysisCache);

            _services.AudioEngine.TrackLoaded += (s, t) =>
            {
                StatusMessage = $"Now playing: {t.Artist} — {t.Title}";
                _ = Bpm.LoadTrackAsync(t.FilePath);
            };
        }

        [RelayCommand]
        private void ToggleLibrary() => IsLibraryVisible = !IsLibraryVisible;

        [RelayCommand]
        private void ToggleQueue() => IsQueueVisible = !IsQueueVisible;

        [RelayCommand]
        private async Task OpenFileAsync()
        {
            StatusMessage = "Opening file…";
            await Task.CompletedTask;
        }

        [RelayCommand]
        private async Task ScanLibraryAsync()
        {
            StatusMessage = "Scanning library…";
            await _services.Library.ScanAllAsync();
            StatusMessage = $"Library: {await _services.Library.CountAsync()} tracks";
        }
    }
}
