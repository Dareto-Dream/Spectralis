using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Spectralis.App.ViewModels;

namespace Spectralis.App.Views;

/// <summary>Small "choose a playlist" prompt — the Library search flow uses this to add a
/// found track (local or Spotify) into an existing playlist. Mirrors NameInputWindow's minimal style.</summary>
public partial class PlaylistPickerWindow : Window
{
    private PlaylistRow? _result;

    public PlaylistPickerWindow()
    {
        InitializeComponent();
    }

    /// <summary>Shows the picker over the non-smart rows in <paramref name="playlists"/>; null if cancelled.</summary>
    public static async Task<PlaylistRow?> PromptAsync(Window owner, IEnumerable<PlaylistRow> playlists)
    {
        var window = new PlaylistPickerWindow();
        window.PlaylistList.ItemsSource = playlists.Where(p => !p.IsSmart).ToList();
        await window.ShowDialog(owner);
        return window._result;
    }

    private void OnAdd(object? sender, RoutedEventArgs e) => Accept();

    private void OnCancel(object? sender, RoutedEventArgs e) => Close();

    private void OnListDoubleTapped(object? sender, TappedEventArgs e) => Accept();

    private void Accept()
    {
        if (PlaylistList.SelectedItem is PlaylistRow row)
        {
            _result = row;
            Close();
        }
    }
}
