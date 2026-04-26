using Avalonia.Controls;
using Avalonia.Interactivity;
using Spectralis.Core.Capsule;

namespace Spectralis.App.Controls
{
    public partial class CapsuleV5Dialog : Window
    {
        public bool ShouldOpen { get; private set; }

        public CapsuleV5Dialog(CapsuleManifest manifest, bool verified)
        {
            InitializeComponent();
            TitleText.Text = manifest.Title;
            ArtistText.Text = manifest.Artist;
            VerifiedText.Text = verified ? "✓ Verified" : "⚠ Unverified";
            VerifiedText.Foreground = verified
                ? Avalonia.Media.Brushes.LightGreen
                : Avalonia.Media.Brushes.Orange;
            TrackList.ItemsSource = manifest.Tracks;
        }

        private void OnOpen(object? sender, RoutedEventArgs e) { ShouldOpen = true; Close(); }
        private void OnCancel(object? sender, RoutedEventArgs e) => Close();
    }
}
