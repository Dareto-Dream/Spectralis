using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Spectralis.Core.Capsule;

namespace Spectralis.App.ViewModels
{
    public partial class CapsuleViewModel : ObservableObject
    {
        private readonly CapsuleReader _reader;
        private readonly string _extractDir;

        [ObservableProperty] private CapsuleMetadata? _metadata;
        [ObservableProperty] private bool _isLoaded;
        [ObservableProperty] private bool _isLoading;
        [ObservableProperty] private string _errorMessage = string.Empty;
        [ObservableProperty] private CapsuleTrackRef? _selectedTrack;

        public ObservableCollection<CapsuleTrackRef> Tracks { get; } = new();
        public string CapsuleTitle => Metadata?.Title ?? string.Empty;
        public string CapsuleArtist => Metadata?.Artist ?? string.Empty;

        public CapsuleViewModel(CapsuleReader reader, string extractDir)
        {
            _reader = reader;
            _extractDir = extractDir;
        }

        public async Task LoadAsync(string capsulePath)
        {
            IsLoading = true;
            ErrorMessage = string.Empty;
            Tracks.Clear();
            IsLoaded = false;

            try
            {
                string extractTo = Path.Combine(_extractDir, Path.GetFileNameWithoutExtension(capsulePath));
                var result = await _reader.ReadAsync(capsulePath, extractTo);

                if (!result.Success || result.Metadata == null)
                {
                    ErrorMessage = result.Error ?? "Unknown error";
                    return;
                }

                Metadata = result.Metadata;
                OnPropertyChanged(nameof(CapsuleTitle));
                OnPropertyChanged(nameof(CapsuleArtist));

                foreach (var track in result.Metadata.Tracks)
                    Tracks.Add(track);

                IsLoaded = true;
            }
            catch (Exception ex) { ErrorMessage = ex.Message; }
            finally { IsLoading = false; }
        }

        [RelayCommand]
        private void Unload()
        {
            Metadata = null;
            Tracks.Clear();
            IsLoaded = false;
        }
    }
}
