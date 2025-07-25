using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Spectralis.App.Services;
using Spectralis.Core.Models;

namespace Spectralis.App.ViewModels
{
    public partial class LibraryViewModel : ViewModelBase
    {
        private readonly ServiceContainer _services;

        [ObservableProperty] private ObservableCollection<TrackInfo> _tracks = new();
        [ObservableProperty] private TrackInfo? _selectedTrack;
        [ObservableProperty] private string _searchQuery = string.Empty;
        [ObservableProperty] private bool _isLoading;

        public LibraryViewModel(ServiceContainer services)
        {
            _services = services;
            _services.Library.TrackAdded += (s, t) => Tracks.Add(t);
            _services.Library.TrackRemoved += (s, t) =>
            {
                for (int i = 0; i < Tracks.Count; i++)
                    if (Tracks[i].FilePath == t.FilePath) { Tracks.RemoveAt(i); break; }
            };
        }

        [RelayCommand]
        private async Task LoadAsync()
        {
            IsLoading = true;
            var all = await _services.Library.GetAllTracksAsync();
            Tracks.Clear();
            foreach (var t in all) Tracks.Add(t);
            IsLoading = false;
        }

        [RelayCommand]
        private async Task SearchAsync()
        {
            if (string.IsNullOrWhiteSpace(SearchQuery))
            {
                await LoadAsync();
                return;
            }
            IsLoading = true;
            var results = await _services.Library.SearchAsync(SearchQuery);
            Tracks.Clear();
            foreach (var t in results) Tracks.Add(t);
            IsLoading = false;
        }

        [RelayCommand]
        private async Task PlaySelectedAsync()
        {
            if (SelectedTrack == null) return;
            await _services.AudioEngine.LoadAsync(SelectedTrack.FilePath);
            _services.AudioEngine.Play();
        }

        [RelayCommand]
        private void EnqueueSelected()
        {
            if (SelectedTrack == null) return;
            _services.Queue.Enqueue(new Core.Queue.PlayQueueItem(SelectedTrack));
        }

        partial void OnSearchQueryChanged(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) _ = LoadAsync();
        }
    }
}
