using Avalonia.Controls;
using Avalonia.Interactivity;
using Spectralis.Core.Settings;

namespace Spectralis.App.Controls
{
    public partial class LyricsSettingsDialog : Window
    {
        public bool Saved { get; private set; }

        public LyricsSettingsDialog(LyricsSettings settings)
        {
            InitializeComponent();
            DataContext = settings;
        }

        private void OnSave(object? sender, RoutedEventArgs e)
        {
            Saved = true;
            Close();
        }

        private void OnCancel(object? sender, RoutedEventArgs e) => Close();
    }
}
