using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Spectralis.App.ViewModels;

namespace Spectralis.App.Views;

public partial class PlaylistsView : UserControl
{
    public PlaylistsView()
    {
        InitializeComponent();
    }

    private PlaylistsViewModel? ViewModel => DataContext as PlaylistsViewModel;

    private Window? OwnerWindow => TopLevel.GetTopLevel(this) as Window;

    private static readonly FilePickerFileType M3uFileType = new("M3U playlists")
    {
        Patterns = ["*.m3u", "*.m3u8"],
    };

    private async void OnNewPlaylist(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is not { } vm || OwnerWindow is not { } owner)
        {
            return;
        }

        var name = await NameInputWindow.PromptAsync(owner, "New Playlist", "Playlist name:", "New Playlist");
        if (!string.IsNullOrWhiteSpace(name))
        {
            var playlist = vm.CreatePlaylist(name, []);
            if (await PlaylistEditorWindow.EditAsync(owner, playlist, vm.BuildItems))
            {
                await vm.SavePlaylist(playlist);
            }
        }
    }

    private async void OnNewSmart(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is not { } vm || OwnerWindow is not { } owner)
        {
            return;
        }

        var name = await NameInputWindow.PromptAsync(owner, "New Smart Playlist", "Smart playlist name:", "New Smart Playlist");
        if (!string.IsNullOrWhiteSpace(name))
        {
            var smart = vm.CreateSmartPlaylist(name);
            if (await SmartPlaylistEditorWindow.EditAsync(owner, smart))
            {
                vm.SaveSmartPlaylist(smart);
            }
        }
    }

    private async void OnImportM3u(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is not { } vm || OwnerWindow is not { } owner)
        {
            return;
        }

        var files = await owner.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Import M3U playlist",
            AllowMultiple = false,
            FileTypeFilter = [M3uFileType, FilePickerFileTypes.All],
        });

        var path = files.FirstOrDefault()?.TryGetLocalPath();
        if (path is not null)
        {
            vm.ImportM3u(path);
        }
    }

    private async void OnRowDoubleTapped(object? sender, Avalonia.Input.TappedEventArgs e)
    {
        if (ViewModel is { SelectedRow: { } row } vm)
        {
            await vm.PlayRowAsync(row);
        }
    }

    private async void OnPlay(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is { SelectedRow: { } row } vm)
        {
            await vm.PlayRowAsync(row);
        }
    }

    private async void OnEdit(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is not { SelectedRow: { } row } vm || OwnerWindow is not { } owner)
        {
            return;
        }

        if (row.IsSmart)
        {
            var smart = vm.FindSmartPlaylist(row.Id);
            if (smart is not null && await SmartPlaylistEditorWindow.EditAsync(owner, smart))
            {
                vm.SaveSmartPlaylist(smart);
            }
        }
        else
        {
            var playlist = vm.FindPlaylist(row.Id);
            if (playlist is not null && await PlaylistEditorWindow.EditAsync(owner, playlist, vm.BuildItems))
            {
                await vm.SavePlaylist(playlist);
            }
        }
    }

    private async void OnExportM3u(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is not { SelectedRow: { } row } vm || OwnerWindow is not { } owner)
        {
            return;
        }

        var file = await owner.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Export M3U playlist",
            SuggestedFileName = $"{row.Name}.m3u8",
            FileTypeChoices = [M3uFileType],
        });

        var path = file?.TryGetLocalPath();
        if (path is not null)
        {
            vm.ExportRow(row, path);
        }
    }

    private async void OnDelete(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is not { SelectedRow: { } row } vm || OwnerWindow is not { } owner)
        {
            return;
        }

        var message = row.IsSpotify
            ? $"Hide \"{row.Name}\" from Spectralis? It stays on your real Spotify account — a future sync brings it back unless you unfollow it there too."
            : $"Delete \"{row.Name}\"? This cannot be undone.";
        var confirmed = await ConfirmWindow.ShowAsync(owner, row.IsSpotify ? "Hide Playlist" : "Delete Playlist", message);
        if (confirmed)
        {
            vm.DeleteRow(row);
        }
    }

    private async void OnRenameLocally(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is not { SelectedRow: { } row } vm || OwnerWindow is not { } owner || row.IsSmart)
        {
            return;
        }

        var name = await NameInputWindow.PromptAsync(owner, "Rename Playlist", "Local name (doesn't rename it on Spotify):", row.Name);
        if (!string.IsNullOrWhiteSpace(name))
        {
            vm.RenameLocally(row, name);
        }
    }

    private async void OnSetCoverImage(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is not { SelectedRow: { } row } vm || OwnerWindow is not { } owner || row.IsSmart)
        {
            return;
        }

        var files = await owner.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Choose cover image",
            AllowMultiple = false,
            FileTypeFilter = [FilePickerFileTypes.ImageAll],
        });

        var path = files.FirstOrDefault()?.TryGetLocalPath();
        if (path is not null)
        {
            vm.SetCoverImage(row, path);
        }
    }

    private async void OnSetDefaultVisualizer(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is not { SelectedRow: { } row } vm ||
            OwnerWindow is not { DataContext: MainWindowViewModel shell } owner ||
            row.IsSmart)
        {
            return;
        }

        var (confirmed, result) = await VisualizerPickerWindow.PromptAsync(owner, shell.NowPlaying.VisualizerOptions);
        if (confirmed)
        {
            vm.SetDefaultVisualizer(row, result);
        }
    }

    private void OnMoveUp(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is { SelectedRow: { } row } vm)
        {
            vm.MoveRow(row, -1);
        }
    }

    private void OnMoveDown(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is { SelectedRow: { } row } vm)
        {
            vm.MoveRow(row, 1);
        }
    }
}
