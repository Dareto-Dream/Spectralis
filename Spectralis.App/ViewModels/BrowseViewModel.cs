using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Spectralis.App.Services;
using Spectralis.Core.Models;

namespace Spectralis.App.ViewModels
{
    public partial class BrowseViewModel : ViewModelBase
    {
        private readonly ServiceContainer _services;

        [ObservableProperty] private ObservableCollection<string> _artists = new();
        [ObservableProperty] private ObservableCollection<string> _albums = new();
        [ObservableProperty] private ObservableCollection<TrackInfo> _tracks = new();
        [ObservableProperty] private string? _selectedArtist;
        [ObservableProperty] private string? _selectedAlbum;
        [ObservableProperty] private BrowseMode _mode = BrowseMode.Artist;

        public BrowseViewModel(ServiceContainer services)
        {
            _services = services;
        }

        [RelayCommand]
        private async Task LoadArtistsAsync()
        {
            var artistSet = await _services.Library.GetArtistsAsync();
            Artists.Clear();
            foreach (var a in artistSet.OrderBy(x => x)) Artists.Add(a);
        }

        [RelayCommand]
        private async Task LoadAlbumsForArtistAsync()
        {
            if (SelectedArtist == null) return;
            var all = await _services.Library.GetAllTracksAsync();
            Albums.Clear();
            foreach (var album in all
                .Where(t => t.Artist == SelectedArtist)
                .Select(t => t.Album)
                .Distinct()
                .OrderBy(x => x))
                Albums.Add(album);
        }

        [RelayCommand]
        private async Task LoadTracksForAlbumAsync()
        {
            if (SelectedAlbum == null) return;
            var all = await _services.Library.GetAllTracksAsync();
            Tracks.Clear();
            foreach (var t in all
                .Where(t => t.Album == SelectedAlbum)
                .OrderBy(t => t.DiscNumber).ThenBy(t => t.TrackNumber))
                Tracks.Add(t);
        }

        partial void OnSelectedArtistChanged(string? value)
        {
            if (value != null) _ = LoadAlbumsForArtistAsync();
        }

        partial void OnSelectedAlbumChanged(string? value)
        {
            if (value != null) _ = LoadTracksForAlbumAsync();
        }
    }

    public enum BrowseMode { Artist, Album, Genre }
}
