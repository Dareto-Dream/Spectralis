using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Spectralis.App.ViewModels;

namespace Spectralis.App.Views;

public partial class PodcastsView : UserControl
{
    public PodcastsView()
    {
        InitializeComponent();
        Loaded += (_, _) => Vm?.RefreshFromDatabase();
    }

    private PodcastsViewModel? Vm => DataContext as PodcastsViewModel;

    private PodcastEpisodeRow? RowFrom(object? sender) =>
        (sender as Control)?.DataContext as PodcastEpisodeRow ?? EpisodeList.SelectedItem as PodcastEpisodeRow;

    private async void OnAddFolderClick(object? sender, RoutedEventArgs e)
    {
        if (Vm is not { } vm || TopLevel.GetTopLevel(this) is not { } topLevel)
        {
            return;
        }

        var folders = await topLevel.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Add podcast folder",
            AllowMultiple = true,
        });

        foreach (var path in folders.Select(f => f.TryGetLocalPath()).Where(p => p is not null))
        {
            vm.AddFolder(path!);
        }
    }

    private async void OnRescanClick(object? sender, RoutedEventArgs e)
    {
        if (Vm is { } vm)
        {
            await vm.RescanAsync();
        }
    }

    private void OnRemoveFolderClick(object? sender, RoutedEventArgs e)
    {
        if (Vm is { } vm && (sender as Control)?.Tag is string folder)
        {
            vm.RemoveFolder(folder);
        }
    }

    private async void OnPlayShowClick(object? sender, RoutedEventArgs e)
    {
        if (Vm is { } vm)
        {
            await vm.PlayShowAsync(vm.SelectedShow);
        }
    }

    private async void OnEpisodePlayClick(object? sender, RoutedEventArgs e)
    {
        if (Vm is { } vm)
        {
            await vm.PlayEpisodeAsync(RowFrom(sender));
        }
    }

    private async void OnEpisodeActivated(object? sender, Avalonia.Input.TappedEventArgs e)
    {
        if (Vm is { } vm && EpisodeList.SelectedItem is PodcastEpisodeRow row)
        {
            await vm.PlayEpisodeAsync(row);
        }
    }

    private void OnMarkFinishedClick(object? sender, RoutedEventArgs e) => Vm?.MarkFinished(RowFrom(sender));

    private void OnMarkUnplayedClick(object? sender, RoutedEventArgs e) => Vm?.MarkUnplayed(RowFrom(sender));

    private void OnRemoveFromPodcastsClick(object? sender, RoutedEventArgs e) => Vm?.RemoveFromPodcasts(RowFrom(sender));
}
