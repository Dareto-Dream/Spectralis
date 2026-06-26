using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Spectralis.App.ViewModels;

namespace Spectralis.App.Views;

public partial class LibraryView : UserControl
{
    public LibraryView()
    {
        InitializeComponent();
    }

    private async void OnAddFolderClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not LibraryViewModel vm || TopLevel.GetTopLevel(this) is not { } topLevel)
        {
            return;
        }

        var folders = await topLevel.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Add music folder",
            AllowMultiple = true,
        });

        var paths = folders
            .Select(folder => folder.TryGetLocalPath())
            .Where(path => path is not null)
            .Select(path => path!)
            .ToList();

        if (paths.Count == 0)
        {
            return;
        }

        // Adding a folder both scans it and registers it as a watched library folder.
        foreach (var path in paths)
        {
            vm.AddWatchedFolder(path);
        }

        await vm.ScanPathsAsync(paths);
    }

    private async void OnRescanClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is LibraryViewModel vm)
        {
            await vm.RescanAsync();
        }
    }

    private async void OnContextPlay(object? sender, RoutedEventArgs e)
    {
        if (DataContext is LibraryViewModel vm)
        {
            await vm.PlayRowAsync(vm.SelectedRow);
        }
    }

    private async void OnContextEditTags(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not LibraryViewModel vm ||
            TopLevel.GetTopLevel(this) is not Window owner)
        {
            return;
        }

        var paths = TrackGrid.SelectedItems
            .OfType<TrackRow>()
            .Select(row => row.Path)
            .Where(File.Exists)
            .ToList();
        if (paths.Count == 0)
        {
            return;
        }

        var saved = paths.Count == 1
            ? await TagEditorWindow.EditAsync(owner, paths[0])
            : await BatchTagEditorWindow.EditAsync(owner, paths);

        if (saved)
        {
            await vm.ScanPathsAsync(paths);
            if (owner.DataContext is MainWindowViewModel shell)
            {
                foreach (var p in paths)
                    await shell.NowPlaying.RefreshCurrentTrackMetadataAsync(p);
            }
        }
    }

    private async void OnContextPlayNext(object? sender, RoutedEventArgs e)
    {
        var paths = TrackGrid.SelectedItems
            .OfType<TrackRow>()
            .Select(row => row.Path)
            .Where(File.Exists)
            .ToList();
        if (paths.Count == 0) return;
        if (TopLevel.GetTopLevel(this) is Window { DataContext: MainWindowViewModel shell })
            await shell.NowPlaying.QueueFilesNextAsync(paths);
    }

    private async void OnContextAddToQueue(object? sender, RoutedEventArgs e)
    {
        var paths = TrackGrid.SelectedItems
            .OfType<TrackRow>()
            .Select(row => row.Path)
            .Where(File.Exists)
            .ToList();
        if (paths.Count == 0) return;
        if (TopLevel.GetTopLevel(this) is Window { DataContext: MainWindowViewModel shell })
            await shell.NowPlaying.QueueFilesAsync(paths, playIfQueueWasEmpty: true);
    }

    private async void OnContextContentWarnings(object? sender, RoutedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this) is not Window owner) return;
        var path = TrackGrid.SelectedItems
            .OfType<TrackRow>()
            .Select(row => row.Path)
            .FirstOrDefault(File.Exists);
        if (path is not null)
            await ContentWarningEditorWindow.ShowAsync(owner, path);
    }

    private async void OnContextAnalyze(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not LibraryViewModel vm)
        {
            return;
        }

        var paths = TrackGrid.SelectedItems
            .OfType<TrackRow>()
            .Select(row => row.Path)
            .ToList();
        if (paths.Count > 0)
        {
            await vm.AnalyzePathsAsync(paths);
        }
    }

    private async void OnGridKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && DataContext is LibraryViewModel vm && vm.SelectedRow is not null)
        {
            e.Handled = true;
            await vm.PlayRowAsync(vm.SelectedRow);
        }
    }

    private async void OnImportLegacyClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is LibraryViewModel vm)
        {
            await vm.ImportLegacyLibraryAsync();
        }
    }

    private void OnDismissLegacyClick(object? sender, RoutedEventArgs e)
    {
        (DataContext as LibraryViewModel)?.DismissLegacyImport();
    }

    private async void OnRowDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is LibraryViewModel vm)
        {
            await vm.PlayRowAsync(vm.SelectedRow);
        }
    }
}
