using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Spectralis.Core.Capsule;

namespace Spectralis.App.ViewModels
{
    public partial class CapsuleV5ViewModel : ObservableObject
    {
        private readonly CapsulePackager _packager = new();
        private readonly CapsuleSigner _signer = new();
        private readonly CapsuleTrustStore _trustStore;

        [ObservableProperty] private CapsuleManifest? _manifest;
        [ObservableProperty] private bool _isVerified;
        [ObservableProperty] private bool _isLoading;
        [ObservableProperty] private string _statusText = string.Empty;

        public ObservableCollection<CapsuleTrack> Tracks { get; } = new();

        public CapsuleV5ViewModel(CapsuleTrustStore trustStore)
        {
            _trustStore = trustStore;
        }

        [RelayCommand]
        private async Task OpenAsync(string path)
        {
            IsLoading = true;
            StatusText = "Loading…";
            try
            {
                Manifest = await _packager.ReadManifestAsync(path);
                if (Manifest == null) { StatusText = "Invalid capsule"; return; }

                IsVerified = _signer.Verify(Manifest);
                Tracks.Clear();
                foreach (var t in Manifest.Tracks) Tracks.Add(t);
                StatusText = IsVerified ? "Verified ✓" : "Unverified — proceed with caution";
            }
            finally { IsLoading = false; }
        }

        [RelayCommand]
        private async Task TrustCurrentAsync()
        {
            if (Manifest?.Trust.PublicKeyBase64 == null) return;
            await _trustStore.TrustAsync(Manifest.Id, Manifest.Trust.PublicKeyBase64);
            StatusText = "Capsule trusted";
        }

        [RelayCommand]
        private void Unload()
        {
            Manifest = null;
            Tracks.Clear();
            IsVerified = false;
            StatusText = string.Empty;
        }
    }
}
