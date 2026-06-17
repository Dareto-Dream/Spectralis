using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Spectralis.Core.AlbumWorld;

namespace Spectralis.App.ViewModels
{
    public partial class AlbumWorldViewModel : ObservableObject
    {
        [ObservableProperty] private string _albumTitle = string.Empty;
        [ObservableProperty] private string _artist = string.Empty;
        [ObservableProperty] private bool _isOpen;
        [ObservableProperty] private WorldTrack? _currentTrack;

        public ObservableCollection<WorldTrack> Tracks { get; } = new();
        public ObservableCollection<string> EarnedAchievements { get; } = new();

        private AlbumWorldManifest? _manifest;
        private AlbumWorldSession? _session;

        public void Load(AlbumWorldManifest manifest, AlbumWorldSession session)
        {
            _manifest = manifest;
            _session = session;
            AlbumTitle = manifest.AlbumTitle;
            Artist = manifest.Artist;
            IsOpen = true;

            Tracks.Clear();
            foreach (var track in manifest.Tracks)
                Tracks.Add(track);

            EarnedAchievements.Clear();
            foreach (var id in session.EarnedAchievements)
                EarnedAchievements.Add(id);
        }

        [RelayCommand]
        private void SelectTrack(WorldTrack track)
        {
            if (track.IsLocked) return;
            CurrentTrack = track;
        }

        [RelayCommand]
        private void Close()
        {
            IsOpen = false;
            CurrentTrack = null;
        }

        public bool IsTrackUnlocked(string trackId) =>
            _session?.Stats.TryGetValue(trackId, out var s) == true && s.Unlocked;
    }
}
