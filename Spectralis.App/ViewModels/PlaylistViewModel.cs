using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Spectralis.App.Services;
using Spectralis.Core.Models;
using Spectralis.Core.Playlists;

namespace Spectralis.App.ViewModels
{
    public partial class PlaylistViewModel : ViewModelBase
    {
        private readonly ServiceContainer _services;

        [ObservableProperty] private ObservableCollection<Playlist> _playlists = new();
        [ObservableProperty] private Playlist? _selectedPlaylist;
        [ObservableProperty] private ObservableCollection<TrackInfo> _playlistTracks = new();
        [ObservableProperty] private string _newPlaylistName = string.Empty;

        public PlaylistViewModel(ServiceContainer services)
        {
            _services = services;
            _services.Playlists.PlaylistsChanged += async (s, e) => await LoadPlaylistsAsync();
        }

        [RelayCommand]
        private async Task LoadPlaylistsAsync()
        {
            var all = await _services.Playlists.GetAllAsync();
            Playlists.Clear();
            foreach (var pl in all) Playlists.Add(pl);
        }

        [RelayCommand]
        private async Task CreatePlaylistAsync()
        {
            if (string.IsNullOrWhiteSpace(NewPlaylistName)) return;
            var pl = new Playlist { Name = NewPlaylistName };
            await _services.Playlists.CreateAsync(pl);
            NewPlaylistName = string.Empty;
        }

        [RelayCommand]
        private async Task DeleteSelectedAsync()
        {
            if (SelectedPlaylist == null) return;
            await _services.Playlists.DeleteAsync(SelectedPlaylist.Id);
            SelectedPlaylist = null;
        }

        [RelayCommand]
        private async Task LoadTracksAsync()
        {
            if (SelectedPlaylist == null) return;
            PlaylistTracks.Clear();
            var paths = await _services.Playlists.GetTrackPathsAsync(SelectedPlaylist.Id);
            foreach (var path in paths)
            {
                var track = await _services.Library.GetByPathAsync(path);
                if (track != null) PlaylistTracks.Add(track);
            }
        }

        [RelayCommand]
        private async Task EnqueuePlaylistAsync()
        {
            foreach (var t in PlaylistTracks)
                _services.Queue.Enqueue(new Core.Queue.PlayQueueItem(t));
        }

        partial void OnSelectedPlaylistChanged(Playlist? value)
        {
            if (value != null) _ = LoadTracksAsync();
        }
    }
}
