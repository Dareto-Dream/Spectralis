using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Spectralis.Core.Models;
using Spectralis.Core.Tags;

namespace Spectralis.App.ViewModels
{
    public partial class TagEditorViewModel : ViewModelBase
    {
        private readonly TagEditorService _service;
        private string? _filePath;

        [ObservableProperty] private string _title = string.Empty;
        [ObservableProperty] private string _artist = string.Empty;
        [ObservableProperty] private string _albumArtist = string.Empty;
        [ObservableProperty] private string _album = string.Empty;
        [ObservableProperty] private string _genre = string.Empty;
        [ObservableProperty] private int _year;
        [ObservableProperty] private int _trackNumber;
        [ObservableProperty] private int _discNumber;
        [ObservableProperty] private bool _isDirty;
        [ObservableProperty] private bool _isSaving;
        [ObservableProperty] private string _statusMessage = string.Empty;

        public TagEditorViewModel(TagEditorService service)
        {
            _service = service;
        }

        public async Task LoadAsync(string filePath)
        {
            _filePath = filePath;
            var info = await _service.ReadTagsAsync(filePath);
            Title = info.Title;
            Artist = info.Artist;
            AlbumArtist = info.AlbumArtist;
            Album = info.Album;
            Genre = info.Genre;
            Year = info.Year;
            TrackNumber = info.TrackNumber;
            DiscNumber = info.DiscNumber;
            IsDirty = false;
            StatusMessage = string.Empty;
        }

        [RelayCommand]
        private async Task SaveAsync()
        {
            if (_filePath == null || !IsDirty) return;
            IsSaving = true;
            try
            {
                var tags = new TrackInfo
                {
                    FilePath = _filePath,
                    Title = Title,
                    Artist = Artist,
                    AlbumArtist = AlbumArtist,
                    Album = Album,
                    Genre = Genre,
                    Year = Year,
                    TrackNumber = TrackNumber,
                    DiscNumber = DiscNumber
                };
                await _service.WriteTagsAsync(_filePath, tags);
                IsDirty = false;
                StatusMessage = "Saved.";
            }
            catch (System.Exception ex)
            {
                StatusMessage = $"Error: {ex.Message}";
            }
            finally
            {
                IsSaving = false;
            }
        }

        partial void OnTitleChanged(string value) => IsDirty = true;
        partial void OnArtistChanged(string value) => IsDirty = true;
        partial void OnAlbumChanged(string value) => IsDirty = true;
        partial void OnGenreChanged(string value) => IsDirty = true;
        partial void OnYearChanged(int value) => IsDirty = true;
        partial void OnTrackNumberChanged(int value) => IsDirty = true;
    }
}
